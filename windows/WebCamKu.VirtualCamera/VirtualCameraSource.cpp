#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <mfvirtualcamera.h>
#include <ks.h>
#include <ksmedia.h>
#include <ksproxy.h>
#include <shlobj.h>
#include <propvarutil.h>
#include <wrl.h>
#include <wrl/implements.h>
#include <algorithm>
#include <atomic>
#include <mutex>
#include <vector>

using namespace Microsoft::WRL;

// {462EBC79-4F6A-438D-8298-1F095BCF7A41}
static const CLSID CLSID_WebCamKuSource =
{ 0x462ebc79, 0x4f6a, 0x438d, { 0x82, 0x98, 0x1f, 0x09, 0x5b, 0xcf, 0x7a, 0x41 } };
static std::atomic<long> g_objects{};
constexpr DWORD Width = 1280, Height = 720, Stride = Width * 4, HeaderSize = 64, FrameBytes = Stride * Height, Nv12Bytes = Width * Height * 3 / 2;
static void Trace(const char*) {}

class CameraSource;

class SharedFrames {
public:
    SharedFrames() {
        wchar_t common[MAX_PATH]{};
        if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_DOCUMENTS, nullptr, SHGFP_TYPE_CURRENT, common))) {
            path_ = common;
            path_ += L"\\WebCamKu\\frames.bin";
        }
    }
    ~SharedFrames() { close(); }

    bool read(BYTE* destination) {
        if (!ensureOpen()) { fallback(destination); return false; }
        auto header = static_cast<volatile LONG64*>(view_);
        FILETIME now{}; GetSystemTimeAsFileTime(&now);
        const auto nowTicks = (static_cast<ULONGLONG>(now.dwHighDateTime) << 32) | now.dwLowDateTime;
        const auto heartbeat = header[5]; // offset 40
        if (heartbeat == 0 || nowTicks > static_cast<ULONGLONG>(heartbeat) + 20'000'000ULL) {
            fallback(destination); return false;
        }
        for(int attempt=0;attempt<4;attempt++) {
            const auto first=header[3]; // offset 24
            if((first&1)||first==0){SwitchToThread();continue;}
            MemoryBarrier(); memcpy(destination,static_cast<BYTE*>(view_)+HeaderSize,FrameBytes); MemoryBarrier();
            if(header[3]==first){if(lastFrame_.empty())lastFrame_.resize(FrameBytes);memcpy(lastFrame_.data(),destination,FrameBytes);return true;}
            SwitchToThread();
        }
        fallback(destination); return false;
    }
private:
    bool ensureOpen() {
        if (view_) return true;
        file_ = CreateFileW(path_.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file_ == INVALID_HANDLE_VALUE) return false;
        mapping_ = CreateFileMappingW(file_, nullptr, PAGE_READONLY, 0, 0, nullptr);
        if (!mapping_) { close(); return false; }
        view_ = MapViewOfFile(mapping_, FILE_MAP_READ, 0, 0, HeaderSize + FrameBytes);
        if (!view_) { close(); return false; }
        if (*static_cast<DWORD*>(view_) != 0x31464B57u) { close(); return false; }
        return true;
    }
    void close() {
        if (view_) UnmapViewOfFile(view_); view_ = nullptr;
        if (mapping_) CloseHandle(mapping_); mapping_ = nullptr;
        if (file_ != INVALID_HANDLE_VALUE) CloseHandle(file_); file_ = INVALID_HANDLE_VALUE;
    }
    static void placeholder(BYTE* pixels) {
        for (DWORD y = 0; y < Height; ++y) for (DWORD x = 0; x < Width; ++x) {
            const auto i = (static_cast<size_t>(y) * Width + x) * 4;
            const bool band = ((x / 80) + (y / 80)) % 2 == 0;
            pixels[i] = band ? 42 : 30; pixels[i + 1] = band ? 48 : 35;
            pixels[i + 2] = band ? 58 : 43; pixels[i + 3] = 255;
        }
    }
    void fallback(BYTE* pixels){if(lastFrame_.size()==FrameBytes)memcpy(pixels,lastFrame_.data(),FrameBytes);else placeholder(pixels);}
    std::wstring path_;
    std::vector<BYTE> lastFrame_;
    HANDLE file_{INVALID_HANDLE_VALUE}, mapping_{};
    void* view_{};
};

class CameraStream final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ChainInterfaces<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>> {
public:
    CameraStream(CameraSource* source, IMFStreamDescriptor* descriptor) : source_(source), descriptor_(descriptor) {
        g_objects++; MFCreateEventQueue(&events_); MFAllocateWorkQueueEx(MF_STANDARD_WORKQUEUE, &workQueue_);
    }
    ~CameraStream() override { if (events_) events_->Shutdown(); if(workQueue_) MFUnlockWorkQueue(workQueue_); g_objects--; }
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* c, IUnknown* s) override { Trace("Stream BeginGetEvent"); return events_->BeginGetEvent(c, s); }
    STDMETHODIMP EndGetEvent(IMFAsyncResult* r, IMFMediaEvent** e) override { Trace("Stream EndGetEvent"); return events_->EndGetEvent(r, e); }
    STDMETHODIMP GetEvent(DWORD f, IMFMediaEvent** e) override { Trace("Stream GetEvent"); return events_->GetEvent(f, e); }
    STDMETHODIMP QueueEvent(MediaEventType t, REFGUID g, HRESULT h, const PROPVARIANT* v) override { return events_->QueueEventParamVar(t, g, h, v); }
    STDMETHODIMP GetMediaSource(IMFMediaSource** source) override;
    STDMETHODIMP GetStreamDescriptor(IMFStreamDescriptor** descriptor) override { Trace("GetStreamDescriptor"); return descriptor_.CopyTo(descriptor); }
    STDMETHODIMP RequestSample(IUnknown* token) override {
        Trace("RequestSample");
        std::scoped_lock lock(gate_);
        if (!running_) return MF_E_INVALIDREQUEST;
        const bool nv12=currentSubtype_==MFVideoFormat_NV12; const DWORD outputBytes=nv12?Nv12Bytes:FrameBytes;
        ComPtr<IMFMediaBuffer> buffer; ComPtr<IMFSample> sample; HRESULT hr;
        if(allocator_) { hr=allocator_->AllocateSample(&sample); if(FAILED(hr))return hr; hr=sample->GetBufferByIndex(0,&buffer); }
        else { hr=MFCreateMemoryBuffer(outputBytes,&buffer);if(SUCCEEDED(hr))hr=MFCreateSample(&sample);if(SUCCEEDED(hr))hr=sample->AddBuffer(buffer.Get()); }
        if(FAILED(hr))return hr;
        BYTE* bytes{}; DWORD capacity{}; hr = buffer->Lock(&bytes, &capacity, nullptr); if (FAILED(hr)) return hr;
        if(nv12) { std::vector<BYTE> bgra(FrameBytes); frames_.read(bgra.data()); ConvertToNv12(bgra.data(),bytes); }
        else frames_.read(bytes);
        buffer->Unlock(); buffer->SetCurrentLength(outputBytes);
        const LONGLONG duration = 10'000'000 / 30;
        sample->SetSampleTime(MFGetSystemTime()); sample->SetSampleDuration(duration);
        if (token) sample->SetUnknown(MFSampleExtension_Token, token);
        return events_->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get());
    }
    STDMETHODIMP SetStreamState(MF_STREAM_STATE state) override { Trace("SetStreamState"); std::scoped_lock lock(gate_); state_=state; running_=state==MF_STREAM_STATE_RUNNING; return S_OK; }
    STDMETHODIMP GetStreamState(MF_STREAM_STATE* state) override { if(!state)return E_POINTER; std::scoped_lock lock(gate_); *state=state_; return S_OK; }
    HRESULT Start(IMFMediaType* type) { if(!type)return E_INVALIDARG; type->GetGUID(MF_MT_SUBTYPE,&currentSubtype_); if(allocator_){auto hr=allocator_->InitializeSampleAllocator(10,type);if(FAILED(hr))return hr;} running_ = true; state_=MF_STREAM_STATE_RUNNING; PROPVARIANT v{}; InitPropVariantFromInt64(MFGetSystemTime(), &v); auto hr=events_->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, &v); PropVariantClear(&v); return hr; }
    HRESULT Stop() { running_ = false; return events_->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr); }
    HRESULT Shutdown() { running_ = false; return events_ ? events_->Shutdown() : S_OK; }
    HRESULT SetAllocator(IUnknown* allocator){return allocator?allocator->QueryInterface(IID_PPV_ARGS(&allocator_)):E_POINTER;}
private:
    static BYTE Clamp(int value){return static_cast<BYTE>(std::clamp(value,0,255));}
    static void ConvertToNv12(const BYTE* source,BYTE* output){
        BYTE* yPlane=output; BYTE* uvPlane=output+Width*Height;
        for(DWORD y=0;y<Height;y++) for(DWORD x=0;x<Width;x++){const BYTE* p=source+(static_cast<size_t>(y)*Width+x)*4; int b=p[0],g=p[1],r=p[2]; yPlane[y*Width+x]=Clamp(((66*r+129*g+25*b+128)>>8)+16);}
        for(DWORD y=0;y<Height;y+=2) for(DWORD x=0;x<Width;x+=2){int r=0,g=0,b=0;for(DWORD dy=0;dy<2;dy++)for(DWORD dx=0;dx<2;dx++){const BYTE* p=source+(static_cast<size_t>(y+dy)*Width+x+dx)*4;b+=p[0];g+=p[1];r+=p[2];}r/=4;g/=4;b/=4;auto i=(y/2)*Width+x;uvPlane[i]=Clamp(((-38*r-74*g+112*b+128)>>8)+128);uvPlane[i+1]=Clamp(((112*r-94*g-18*b+128)>>8)+128);}
    }
    CameraSource* source_{};
    ComPtr<IMFStreamDescriptor> descriptor_;
    ComPtr<IMFMediaEventQueue> events_;
    ComPtr<IMFVideoSampleAllocator> allocator_;
    SharedFrames frames_;
    std::mutex gate_;
    bool running_{};
    MF_STREAM_STATE state_{MF_STREAM_STATE_STOPPED};
    GUID currentSubtype_{MFVideoFormat_NV12};
    DWORD workQueue_{};
};

class CameraSource final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ChainInterfaces<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>, IMFGetService, IKsControl, IMFSampleAllocatorControl> {
public:
    CameraSource() { g_objects++; }
    ~CameraSource() override { Shutdown(); g_objects--; }
    HRESULT Initialize(IMFAttributes* activationAttributes) {
        HRESULT hr = MFCreateEventQueue(&events_); if (FAILED(hr)) return hr;
        if (FAILED(hr = MFCreateAttributes(&attributes_, 8))) return hr;
        if (activationAttributes) activationAttributes->CopyAllItems(attributes_.Get());
        attributes_->SetUINT32(MF_LOW_LATENCY,TRUE);
        ComPtr<IMFSensorProfileCollection> profiles; ComPtr<IMFSensorProfile> profile;
        if (FAILED(hr=MFCreateSensorProfileCollection(&profiles))) return hr;
        if (FAILED(hr=MFCreateSensorProfile(KSCAMERAPROFILE_Legacy,0,nullptr,&profile))) return hr;
        if (FAILED(hr=profile->AddProfileFilter(0,L"((RES==;FRT<=30,1;SUT==))"))) return hr;
        if (FAILED(hr=profiles->AddProfile(profile.Get()))) return hr;
        if (FAILED(hr=attributes_->SetUnknown(MF_DEVICEMFT_SENSORPROFILE_COLLECTION,profiles.Get()))) return hr;
        if (FAILED(hr=MFCreateAttributes(&streamAttributes_,8))) return hr;
        streamAttributes_->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY,PINNAME_VIDEO_CAPTURE);
        streamAttributes_->SetUINT32(MF_DEVICESTREAM_STREAM_ID,0);
        streamAttributes_->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED,1);
        streamAttributes_->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES,MFFrameSourceTypes_Color);
        streamAttributes_->SetUINT32(MF_LOW_LATENCY,TRUE);
        ComPtr<IMFMediaType> nv12,type; if (FAILED(hr = CreateVideoType(MFVideoFormat_NV12,Nv12Bytes,&nv12))) return hr;
        if (FAILED(hr = CreateVideoType(MFVideoFormat_RGB32,FrameBytes,&type))) return hr;
        IMFMediaType* types[] = { nv12.Get(), type.Get() };
        if (FAILED(hr = MFCreateStreamDescriptor(0, 2, types, &descriptor_))) return hr;
        ComPtr<IMFAttributes> descriptorAttributes; if(FAILED(hr=descriptor_.As(&descriptorAttributes)))return hr;
        streamAttributes_->CopyAllItems(descriptorAttributes.Get());
        ComPtr<IMFMediaTypeHandler> handler; descriptor_->GetMediaTypeHandler(&handler); handler->SetCurrentMediaType(nv12.Get());
        stream_ = Make<CameraStream>(this, descriptor_.Get()); if (!stream_) return E_OUTOFMEMORY;
        IMFStreamDescriptor* descriptors[] = { descriptor_.Get() };
        if (FAILED(hr = MFCreatePresentationDescriptor(1, descriptors, &presentation_))) return hr;
        presentation_->SelectStream(0); return S_OK;
    }
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* c, IUnknown* s) override { Trace("Source BeginGetEvent"); return events_ ? events_->BeginGetEvent(c,s) : MF_E_SHUTDOWN; }
    STDMETHODIMP EndGetEvent(IMFAsyncResult* r, IMFMediaEvent** e) override { Trace("Source EndGetEvent"); return events_ ? events_->EndGetEvent(r,e) : MF_E_SHUTDOWN; }
    STDMETHODIMP GetEvent(DWORD f, IMFMediaEvent** e) override { Trace("Source GetEvent"); return events_ ? events_->GetEvent(f,e) : MF_E_SHUTDOWN; }
    STDMETHODIMP QueueEvent(MediaEventType t, REFGUID g, HRESULT h, const PROPVARIANT* v) override { return events_ ? events_->QueueEventParamVar(t,g,h,v) : MF_E_SHUTDOWN; }
    STDMETHODIMP GetCharacteristics(DWORD* value) override { Trace("GetCharacteristics"); if (!value) return E_POINTER; *value=MFMEDIASOURCE_IS_LIVE; return S_OK; }
    STDMETHODIMP CreatePresentationDescriptor(IMFPresentationDescriptor** value) override { Trace("CreatePresentationDescriptor"); if (!value) return E_POINTER; return presentation_ ? presentation_->Clone(value) : MF_E_SHUTDOWN; }
    STDMETHODIMP Start(IMFPresentationDescriptor* requested, const GUID* format, const PROPVARIANT* position) override {
        Trace("Source Start");
        if (!requested || !position) return E_INVALIDARG; if(format && *format!=GUID_NULL)return MF_E_UNSUPPORTED_TIME_FORMAT;
        if (shutdown_) return MF_E_SHUTDOWN;
        BOOL selected{}; ComPtr<IMFStreamDescriptor> requestedDescriptor; auto hr=requested->GetStreamDescriptorByIndex(0,&selected,&requestedDescriptor);if(FAILED(hr)||!selected)return FAILED(hr)?hr:MF_E_INVALIDREQUEST;
        ComPtr<IMFMediaTypeHandler> requestedHandler; ComPtr<IMFMediaType> requestedType;if(FAILED(hr=requestedDescriptor->GetMediaTypeHandler(&requestedHandler)))return hr;if(FAILED(hr=requestedHandler->GetCurrentMediaType(&requestedType)))return hr;
        events_->QueueEventParamUnk(started_ ? MEUpdatedStream : MENewStream, GUID_NULL, S_OK, stream_.Get());
        hr=stream_->Start(requestedType.Get()); if (FAILED(hr)) return hr;
        PROPVARIANT v{}; InitPropVariantFromInt64(0, &v); hr=events_->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, &v); PropVariantClear(&v); started_=true; return hr;
    }
    STDMETHODIMP Stop() override { if (shutdown_) return MF_E_SHUTDOWN; stream_->Stop(); started_=false; return events_->QueueEventParamVar(MESourceStopped,GUID_NULL,S_OK,nullptr); }
    STDMETHODIMP Pause() override { return MF_E_INVALID_STATE_TRANSITION; }
    STDMETHODIMP Shutdown() override { if (shutdown_) return S_OK; shutdown_=true; if(stream_) stream_->Shutdown(); return events_ ? events_->Shutdown() : S_OK; }
    STDMETHODIMP GetSourceAttributes(IMFAttributes** value) override {Trace("GetSourceAttributes"); return attributes_.CopyTo(value); }
    STDMETHODIMP GetStreamAttributes(DWORD id, IMFAttributes** value) override {Trace("GetStreamAttributes"); if(id!=0) return MF_E_INVALIDSTREAMNUMBER; return streamAttributes_.CopyTo(value); }
    STDMETHODIMP SetD3DManager(IUnknown*) override { Trace("SetD3DManager"); return E_NOTIMPL; }
    STDMETHODIMP GetService(REFGUID,REFIID,void** value) override {Trace("GetService");if(!value)return E_POINTER;*value=nullptr;return MF_E_UNSUPPORTED_SERVICE;}
    STDMETHODIMP KsProperty(PKSPROPERTY property,ULONG propertyLength,void*,ULONG,ULONG* returned) override {Trace("KsProperty");if(returned)*returned=0;if(!property||propertyLength<sizeof(KSPROPERTY))return E_INVALIDARG;return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);}
    STDMETHODIMP KsMethod(PKSMETHOD,ULONG,void*,ULONG,ULONG*) override {Trace("KsMethod");return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);}
    STDMETHODIMP KsEvent(PKSEVENT,ULONG,void*,ULONG,ULONG*) override {Trace("KsEvent");return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);}
    STDMETHODIMP SetDefaultAllocator(DWORD streamId,IUnknown* allocator) override {Trace("SetDefaultAllocator");return streamId==0?stream_->SetAllocator(allocator):MF_E_INVALIDSTREAMNUMBER;}
    STDMETHODIMP GetAllocatorUsage(DWORD streamId,DWORD* inputStreamId,MFSampleAllocatorUsage* usage) override {Trace("GetAllocatorUsage");if(!inputStreamId||!usage)return E_POINTER;if(streamId!=0)return MF_E_INVALIDSTREAMNUMBER;*inputStreamId=0;*usage=MFSampleAllocatorUsage_UsesProvidedAllocator;return S_OK;}
private:
    static HRESULT CreateVideoType(REFGUID subtype,DWORD sampleBytes,IMFMediaType** result){ComPtr<IMFMediaType> value;auto hr=MFCreateMediaType(&value);if(FAILED(hr))return hr;value->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video);value->SetGUID(MF_MT_SUBTYPE,subtype);value->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive);value->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT,TRUE);value->SetUINT32(MF_MT_AVG_BITRATE,sampleBytes*8*30);value->SetUINT32(MF_MT_SAMPLE_SIZE,sampleBytes);MFSetAttributeSize(value.Get(),MF_MT_FRAME_SIZE,Width,Height);MFSetAttributeRatio(value.Get(),MF_MT_FRAME_RATE,30,1);MFSetAttributeRatio(value.Get(),MF_MT_PIXEL_ASPECT_RATIO,1,1);return value.CopyTo(result);}
    ComPtr<IMFMediaEventQueue> events_; ComPtr<IMFAttributes> attributes_, streamAttributes_;
    ComPtr<IMFStreamDescriptor> descriptor_; ComPtr<IMFPresentationDescriptor> presentation_;
    ComPtr<CameraStream> stream_; bool started_{}, shutdown_{};
};

STDMETHODIMP CameraStream::GetMediaSource(IMFMediaSource** source) {
    if (!source) return E_POINTER; return source_->QueryInterface(IID_PPV_ARGS(source));
}

#define ATTR_METHODS \
STDMETHODIMP GetItem(REFGUID k, PROPVARIANT* v) override{return attrs_->GetItem(k,v);} \
STDMETHODIMP GetItemType(REFGUID k, MF_ATTRIBUTE_TYPE* v) override{return attrs_->GetItemType(k,v);} \
STDMETHODIMP CompareItem(REFGUID k, REFPROPVARIANT v, BOOL* r) override{return attrs_->CompareItem(k,v,r);} \
STDMETHODIMP Compare(IMFAttributes* a, MF_ATTRIBUTES_MATCH_TYPE t, BOOL* r) override{return attrs_->Compare(a,t,r);} \
STDMETHODIMP GetUINT32(REFGUID k, UINT32* v) override{return attrs_->GetUINT32(k,v);} \
STDMETHODIMP GetUINT64(REFGUID k, UINT64* v) override{return attrs_->GetUINT64(k,v);} \
STDMETHODIMP GetDouble(REFGUID k, double* v) override{return attrs_->GetDouble(k,v);} \
STDMETHODIMP GetGUID(REFGUID k, GUID* v) override{return attrs_->GetGUID(k,v);} \
STDMETHODIMP GetStringLength(REFGUID k, UINT32* v) override{return attrs_->GetStringLength(k,v);} \
STDMETHODIMP GetString(REFGUID k, LPWSTR v, UINT32 s, UINT32* n) override{return attrs_->GetString(k,v,s,n);} \
STDMETHODIMP GetAllocatedString(REFGUID k, LPWSTR* v, UINT32* n) override{return attrs_->GetAllocatedString(k,v,n);} \
STDMETHODIMP GetBlobSize(REFGUID k, UINT32* v) override{return attrs_->GetBlobSize(k,v);} \
STDMETHODIMP GetBlob(REFGUID k, UINT8* v, UINT32 s, UINT32* n) override{return attrs_->GetBlob(k,v,s,n);} \
STDMETHODIMP GetAllocatedBlob(REFGUID k, UINT8** v, UINT32* n) override{return attrs_->GetAllocatedBlob(k,v,n);} \
STDMETHODIMP GetUnknown(REFGUID k, REFIID i, void** v) override{return attrs_->GetUnknown(k,i,v);} \
STDMETHODIMP SetItem(REFGUID k, REFPROPVARIANT v) override{return attrs_->SetItem(k,v);} \
STDMETHODIMP DeleteItem(REFGUID k) override{return attrs_->DeleteItem(k);} \
STDMETHODIMP DeleteAllItems() override{return attrs_->DeleteAllItems();} \
STDMETHODIMP SetUINT32(REFGUID k, UINT32 v) override{return attrs_->SetUINT32(k,v);} \
STDMETHODIMP SetUINT64(REFGUID k, UINT64 v) override{return attrs_->SetUINT64(k,v);} \
STDMETHODIMP SetDouble(REFGUID k, double v) override{return attrs_->SetDouble(k,v);} \
STDMETHODIMP SetGUID(REFGUID k, REFGUID v) override{return attrs_->SetGUID(k,v);} \
STDMETHODIMP SetString(REFGUID k, LPCWSTR v) override{return attrs_->SetString(k,v);} \
STDMETHODIMP SetBlob(REFGUID k, const UINT8* v, UINT32 s) override{return attrs_->SetBlob(k,v,s);} \
STDMETHODIMP SetUnknown(REFGUID k, IUnknown* v) override{return attrs_->SetUnknown(k,v);} \
STDMETHODIMP LockStore() override{return attrs_->LockStore();} STDMETHODIMP UnlockStore() override{return attrs_->UnlockStore();} \
STDMETHODIMP GetCount(UINT32* v) override{return attrs_->GetCount(v);} \
STDMETHODIMP GetItemByIndex(UINT32 i, GUID* k, PROPVARIANT* v) override{return attrs_->GetItemByIndex(i,k,v);} \
STDMETHODIMP CopyAllItems(IMFAttributes* v) override{return attrs_->CopyAllItems(v);}

class CameraActivate final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ChainInterfaces<IMFActivate, IMFAttributes>> {
public:
    CameraActivate(){g_objects++; MFCreateAttributes(&attrs_,4);attrs_->SetUINT32(MF_VIRTUALCAMERA_PROVIDE_ASSOCIATED_CAMERA_SOURCES,1);} ~CameraActivate() override{g_objects--;}
    STDMETHODIMP ActivateObject(REFIID iid, void** value) override { Trace("ActivateObject"); if(!value)return E_POINTER; auto source=Make<CameraSource>(); if(!source)return E_OUTOFMEMORY; auto hr=source->Initialize(attrs_.Get()); Trace(FAILED(hr)?"ActivateObject initialize failed":"ActivateObject initialized"); if(FAILED(hr))return hr; hr=source.CopyTo(iid,value); Trace(FAILED(hr)?"ActivateObject QI failed":"ActivateObject done"); return hr; }
    STDMETHODIMP ShutdownObject() override{return S_OK;} STDMETHODIMP DetachObject() override{return S_OK;}
    ATTR_METHODS
private: ComPtr<IMFAttributes> attrs_;
};

class CameraFactory final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IClassFactory> {
public:
    CameraFactory(){g_objects++;} ~CameraFactory() override{g_objects--;}
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** value) override { if(outer)return CLASS_E_NOAGGREGATION; auto activate=Make<CameraActivate>(); return activate?activate.CopyTo(iid,value):E_OUTOFMEMORY; }
    STDMETHODIMP LockServer(BOOL lock) override { g_objects += lock?1:-1; return S_OK; }
};

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID){ return TRUE; }
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** value){ if(clsid!=CLSID_WebCamKuSource)return CLASS_E_CLASSNOTAVAILABLE; auto factory=Make<CameraFactory>(); return factory?factory.CopyTo(iid,value):E_OUTOFMEMORY; }
extern "C" HRESULT __stdcall DllCanUnloadNow(){ return g_objects.load()==0?S_OK:S_FALSE; }
