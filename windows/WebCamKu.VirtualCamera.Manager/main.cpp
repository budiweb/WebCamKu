#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mfvirtualcamera.h>
#include <mferror.h>
#include <initguid.h>
#include <devpropdef.h>
#include <sddl.h>
#include <wrl.h>
#include <iostream>
#include <string>
#include <fstream>

using Microsoft::WRL::ComPtr;
constexpr wchar_t SourceId[] = L"{462EBC79-4F6A-438D-8298-1F095BCF7A41}";
constexpr wchar_t CameraName[] = L"WebCamKu Camera";
constexpr wchar_t StopEventName[] = L"Local\\WebCamKu.VirtualCamera.Stop";
constexpr wchar_t ReadyEventName[] = L"Local\\WebCamKu.VirtualCamera.Ready";
constexpr wchar_t StartEventName[] = L"Local\\WebCamKu.VirtualCamera.Start";
constexpr wchar_t ShutdownEventName[] = L"Local\\WebCamKu.VirtualCamera.Shutdown";
static const CLSID SourceClsid={0x462ebc79,0x4f6a,0x438d,{0x82,0x98,0x1f,0x09,0x5b,0xcf,0x7a,0x41}};
DEFINE_DEVPROPKEY(PKEY_VCamSourceId,0x6ac1fbf7,0x45f7,0x4e06,0xbd,0xa7,0xf8,0x17,0xeb,0xfa,0x04,0xd1,4);
DEFINE_DEVPROPKEY(PKEY_VCamFriendlyName,0x6ac1fbf7,0x45f7,0x4e06,0xbd,0xa7,0xf8,0x17,0xeb,0xfa,0x04,0xd1,5);
DEFINE_DEVPROPKEY(PKEY_VCamLifetime,0x6ac1fbf7,0x45f7,0x4e06,0xbd,0xa7,0xf8,0x17,0xeb,0xfa,0x04,0xd1,6);
DEFINE_DEVPROPKEY(PKEY_VCamAccess,0x6ac1fbf7,0x45f7,0x4e06,0xbd,0xa7,0xf8,0x17,0xeb,0xfa,0x04,0xd1,7);
static void Log(const wchar_t* stage,HRESULT hr=S_OK){wchar_t root[MAX_PATH]{};GetEnvironmentVariableW(L"PUBLIC",root,MAX_PATH);std::wofstream file(std::wstring(root)+L"\\Documents\\WebCamKu-vcam.log",std::ios::app);file<<stage<<L" 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L"\n";}

static HRESULT OpenVirtualCamera(IMFVirtualCamera** camera) {
    return MFCreateVirtualCamera(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_System,
        MFVirtualCameraAccess_CurrentUser, CameraName, SourceId, nullptr, 0, camera);
}
static bool IsAdministrator(){BOOL admin=FALSE;SID_IDENTIFIER_AUTHORITY authority=SECURITY_NT_AUTHORITY;PSID sid{};if(AllocateAndInitializeSid(&authority,2,SECURITY_BUILTIN_DOMAIN_RID,DOMAIN_ALIAS_RID_ADMINS,0,0,0,0,0,0,&sid)){CheckTokenMembership(nullptr,sid,&admin);FreeSid(sid);}return admin==TRUE;}

static HRESULT RegisterCamera(bool stopOnly, bool remove) {
    Log(L"RegisterCamera begin"); ComPtr<IMFVirtualCamera> camera; HRESULT hr=OpenVirtualCamera(&camera); Log(L"MFCreateVirtualCamera",hr); if(FAILED(hr))return hr;
    wchar_t* existing{}; UINT32 existingLength{};
    const bool isNew=FAILED(camera->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,&existing,&existingLength));
    CoTaskMemFree(existing);
    if(isNew && !stopOnly && !remove && IsAdministrator()) {
        const auto lifetime=MFVirtualCameraLifetime_System; const auto access=MFVirtualCameraAccess_CurrentUser;
        if(FAILED(hr=camera->AddProperty(&PKEY_VCamSourceId,DEVPROP_TYPE_STRING,reinterpret_cast<const BYTE*>(SourceId),sizeof(SourceId))))return hr;
        if(FAILED(hr=camera->AddProperty(&PKEY_VCamFriendlyName,DEVPROP_TYPE_STRING,reinterpret_cast<const BYTE*>(CameraName),sizeof(CameraName))))return hr;
        if(FAILED(hr=camera->AddProperty(&PKEY_VCamLifetime,DEVPROP_TYPE_INT32,reinterpret_cast<const BYTE*>(&lifetime),sizeof(lifetime))))return hr;
        if(FAILED(hr=camera->AddProperty(&PKEY_VCamAccess,DEVPROP_TYPE_INT32,reinterpret_cast<const BYTE*>(&access),sizeof(access))))return hr;
    }
    Log(L"properties complete"); if(remove) hr=camera->Remove(); else if(stopOnly) hr=camera->Stop(); else hr=camera->Start(nullptr); Log(L"camera operation returned",hr);
    if(remove || stopOnly) camera->Shutdown();
    return hr;
}

static HRESULT LaunchHost() {
    HANDLE existing=OpenEventW(SYNCHRONIZE,FALSE,ReadyEventName);
    if(existing){CloseHandle(existing);HANDLE start=OpenEventW(EVENT_MODIFY_STATE,FALSE,StartEventName);if(!start)return HRESULT_FROM_WIN32(GetLastError());SetEvent(start);CloseHandle(start);return S_OK;}
    PSECURITY_DESCRIPTOR descriptor{}; ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:(A;;GA;;;WD)",SDDL_REVISION_1,&descriptor,nullptr);
    SECURITY_ATTRIBUTES security{sizeof(security),descriptor,FALSE};
    const wchar_t* names[]={StopEventName,StartEventName,ShutdownEventName,ReadyEventName};
    HANDLE events[4]{};for(int i=0;i<4;i++){events[i]=CreateEventW(descriptor?&security:nullptr,TRUE,FALSE,names[i]);if(!events[i]){if(descriptor)LocalFree(descriptor);for(auto event:events)if(event)CloseHandle(event);return HRESULT_FROM_WIN32(GetLastError());}ResetEvent(events[i]);}if(descriptor)LocalFree(descriptor);HANDLE ready=events[3];
    wchar_t executable[MAX_PATH]{}; GetModuleFileNameW(nullptr,executable,MAX_PATH);
    std::wstring command=L"\""+std::wstring(executable)+L"\" host"; STARTUPINFOW startup{sizeof(startup)}; PROCESS_INFORMATION process{};
    if(!CreateProcessW(executable,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW|DETACHED_PROCESS,nullptr,nullptr,&startup,&process)) {CloseHandle(ready);return HRESULT_FROM_WIN32(GetLastError());}
    HANDLE waits[]={ready,process.hProcess}; DWORD result=WaitForMultipleObjects(2,waits,FALSE,15'000); HRESULT hr=S_OK;
    if(result==WAIT_OBJECT_0+1){DWORD code{};GetExitCodeProcess(process.hProcess,&code);hr=code?E_FAIL:S_OK;}
    else if(result!=WAIT_OBJECT_0)hr=HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    CloseHandle(process.hThread);CloseHandle(process.hProcess);for(auto event:events)CloseHandle(event);return hr;
}

static HRESULT RunHost() {
    HANDLE stop=OpenEventW(SYNCHRONIZE|EVENT_MODIFY_STATE,FALSE,StopEventName), start=OpenEventW(SYNCHRONIZE|EVENT_MODIFY_STATE,FALSE,StartEventName), shutdown=OpenEventW(SYNCHRONIZE,FALSE,ShutdownEventName), ready=OpenEventW(EVENT_MODIFY_STATE,FALSE,ReadyEventName);
    if(!stop||!start||!shutdown||!ready)return HRESULT_FROM_WIN32(GetLastError());
    Log(L"host begin"); ComPtr<IMFVirtualCamera> camera; auto hr=OpenVirtualCamera(&camera);if(SUCCEEDED(hr))hr=camera->Start(nullptr);Log(L"host register returned",hr);
    if(SUCCEEDED(hr)){SetEvent(ready);Log(L"host ready");HANDLE waits[]={stop,start,shutdown};for(;;){DWORD result=WaitForMultipleObjects(3,waits,FALSE,INFINITE);if(result==WAIT_OBJECT_0){ResetEvent(stop);hr=camera->Stop();Log(L"host stop",hr);}else if(result==WAIT_OBJECT_0+1){ResetEvent(start);hr=camera->Start(nullptr);Log(L"host start",hr);}else break;}camera->Stop();camera->Shutdown();}
    CloseHandle(ready);CloseHandle(shutdown);CloseHandle(start);CloseHandle(stop);return hr;
}

static HRESULT StopHost() {
    HANDLE stop=OpenEventW(EVENT_MODIFY_STATE,FALSE,StopEventName); if(!stop)return S_OK; SetEvent(stop);CloseHandle(stop);return S_OK;
}

static HRESULT ShutdownHost() {
    HANDLE shutdown=OpenEventW(EVENT_MODIFY_STATE,FALSE,ShutdownEventName);if(!shutdown)return S_OK;SetEvent(shutdown);CloseHandle(shutdown);
    for(int i=0;i<50;i++){HANDLE ready=OpenEventW(SYNCHRONIZE,FALSE,ReadyEventName);if(!ready)return S_OK;CloseHandle(ready);Sleep(100);}return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
}

static HRESULT FindCamera(IMFActivate** result) {
    *result=nullptr; ComPtr<IMFAttributes> attributes; HRESULT hr=MFCreateAttributes(&attributes,1); if(FAILED(hr))return hr;
    attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    IMFActivate** devices{}; UINT32 count{}; hr=MFEnumDeviceSources(attributes.Get(),&devices,&count); if(FAILED(hr))return hr;
    for(UINT32 i=0;i<count;i++) {
        wchar_t* name{}; UINT32 length{};
        if(SUCCEEDED(devices[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,&name,&length))) {
            std::wcout<<L"Camera: "<<name<<L"\n";
            if(wcsstr(name,CameraName)!=nullptr) devices[i]->QueryInterface(IID_PPV_ARGS(result));
            CoTaskMemFree(name);
        }
        devices[i]->Release();
    }
    CoTaskMemFree(devices); return *result?S_OK:MF_E_NOT_FOUND;
}

static HRESULT TestConsumer() {
    ComPtr<IMFActivate> activate; HRESULT hr=FindCamera(&activate); if(FAILED(hr))return hr;
    ComPtr<IMFMediaSource> source; hr=activate->ActivateObject(IID_PPV_ARGS(&source)); std::wcout<<L"Activate device: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L"\n"; if(FAILED(hr))return hr;
    ComPtr<IMFSourceReader> reader; hr=MFCreateSourceReaderFromMediaSource(source.Get(),nullptr,&reader); std::wcout<<L"Create reader: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L"\n"; if(FAILED(hr))return hr;
    hr=reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),TRUE); std::wcout<<L"Select video: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L"\n"; if(FAILED(hr))return hr;
    ComPtr<IMFMediaType> requested; MFCreateMediaType(&requested); requested->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video); requested->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_NV12);
    hr=reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),nullptr,requested.Get()); std::wcout<<L"Set NV12: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L"\n"; if(FAILED(hr))return hr;
    DWORD stream{},flags{}; LONGLONG timestamp{}; ComPtr<IMFSample> sample;
    int samples=0, changingFrames=0; UINT64 firstHash=0,lastHash=0;
    for(int i=0;i<100 && samples<30;i++) {
        sample.Reset(); hr=reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),0,&stream,&flags,&timestamp,&sample);
        if(FAILED(hr))return hr;
        if(!sample){std::wcout<<L"Read event "<<i<<L": flags=0x"<<std::hex<<flags<<L"\n";continue;}
        samples++; ComPtr<IMFMediaBuffer> current; if(SUCCEEDED(sample->ConvertToContiguousBuffer(&current))){BYTE* data{};DWORD length{};if(SUCCEEDED(current->Lock(&data,nullptr,&length))){UINT64 hash=1469598103934665603ULL;for(DWORD j=0;j<length;j+=4096){hash^=data[j];hash*=1099511628211ULL;}current->Unlock();if(samples==1)firstHash=hash;else if(hash!=lastHash)changingFrames++;lastHash=hash;}}
    }
    if(samples<30)return E_FAIL;
    ComPtr<IMFMediaBuffer> buffer; sample->ConvertToContiguousBuffer(&buffer); DWORD length{}; buffer->GetCurrentLength(&length);
    std::wcout << L"Opened WebCamKu Camera: 30 samples, last timestamp " << timestamp << L", bytes " << length << L", changing frames " << std::dec << changingFrames << L", hash 0x" << std::hex << lastHash << L"\n";
    source->Shutdown(); activate->ShutdownObject(); return length>0?S_OK:E_FAIL;
}

static HRESULT TestSource() {
    ComPtr<IUnknown> unknown; HRESULT hr=CoCreateInstance(SourceClsid,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&unknown));
    std::wcout<<L"CoCreate IUnknown: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<std::endl;
    ComPtr<IMFActivate> activate; if(SUCCEEDED(hr))hr=unknown.As(&activate);
    std::wcout<<L"Query IMFActivate: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<std::endl;
    if(FAILED(hr))return hr;
    ComPtr<IMFMediaSource> source; if(FAILED(hr=activate->ActivateObject(IID_PPV_ARGS(&source))))return hr;
    ComPtr<IMFPresentationDescriptor> descriptor; hr=source->CreatePresentationDescriptor(&descriptor);
    if(SUCCEEDED(hr))std::wcout<<L"Media source activated and presentation descriptor created\n";
    ComPtr<IMFSourceReader> reader; if(SUCCEEDED(hr))hr=MFCreateSourceReaderFromMediaSource(source.Get(),nullptr,&reader);
    if(SUCCEEDED(hr)){DWORD stream{},flags{};LONGLONG timestamp{};ComPtr<IMFSample> sample;hr=reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),0,&stream,&flags,&timestamp,&sample);std::wcout<<L"Direct source read: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L" flags 0x"<<flags<<L" sample "<<(sample?1:0)<<std::endl;}
    source->Shutdown(); return hr;
}

static HRESULT TestDll(const wchar_t* path) {
    auto module=LoadLibraryW(path); if(!module)return HRESULT_FROM_WIN32(GetLastError());
    using GetClass=HRESULT(__stdcall*)(REFCLSID,REFIID,void**);
    auto getClass=reinterpret_cast<GetClass>(GetProcAddress(module,"DllGetClassObject")); if(!getClass)return HRESULT_FROM_WIN32(GetLastError());
    ComPtr<IClassFactory> factory; auto hr=getClass(SourceClsid,IID_PPV_ARGS(&factory));
    std::wcout<<L"DllGetClassObject: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<L"\n";
    if(SUCCEEDED(hr)){ComPtr<IMFActivate> activate; hr=factory->CreateInstance(nullptr,IID_PPV_ARGS(&activate)); std::wcout<<L"Create IMFActivate: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<std::endl; activate.Reset();}
    factory.Reset(); FreeLibrary(module); return hr;
}

int wmain(int argc,wchar_t** argv) {
    CoInitializeEx(nullptr,COINIT_MULTITHREADED); HRESULT hr=MFStartup(MF_VERSION);
    std::wstring command=argc>1?argv[1]:L"status";
    if(SUCCEEDED(hr)) {
        if(command==L"start") hr=LaunchHost();
        else if(command==L"host") hr=RunHost();
        else if(command==L"stop") hr=StopHost();
        else if(command==L"shutdown") hr=ShutdownHost();
        else if(command==L"remove") {ShutdownHost();hr=RegisterCamera(false,true);}
        else if(command==L"test") hr=TestConsumer();
        else if(command==L"test-source") hr=TestSource();
        else if(command==L"test-dll" && argc>2) hr=TestDll(argv[2]);
        else { ComPtr<IMFActivate> camera; hr=FindCamera(&camera); if(SUCCEEDED(hr))std::wcout<<CameraName<<L" is registered\n"; }
    }
    if(FAILED(hr)) std::wcerr << L"Virtual camera command failed: 0x" << std::hex << static_cast<unsigned long>(hr) << L"\n";
    MFShutdown(); CoUninitialize(); return FAILED(hr)?1:0;
}
