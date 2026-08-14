#include <windows.h>
#include <obs-module.h>
#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

OBS_DECLARE_MODULE()

constexpr DWORD Width=1280,Height=720,Stride=Width*4,HeaderSize=64,FrameBytes=Stride*Height;
using RegisterSourceFn=void(*)(const obs_source_info*,size_t);
using OutputVideoFn=void(*)(obs_source_t*,const obs_source_frame*);
static OutputVideoFn outputVideo{};

struct WebCamKuSource {
    obs_source_t* source{};
    std::atomic<bool> running{true};
    std::thread worker;
    HANDLE file{INVALID_HANDLE_VALUE},mapping{};
    void* view{};
    std::vector<uint8_t> pixels=std::vector<uint8_t>(FrameBytes);
    bool hasFrame{};

    explicit WebCamKuSource(obs_source_t* value):source(value),worker([this]{run();}){}
    ~WebCamKuSource(){running=false;if(worker.joinable())worker.join();closeMapping();}
    void closeMapping(){if(view)UnmapViewOfFile(view);view=nullptr;if(mapping)CloseHandle(mapping);mapping=nullptr;if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);file=INVALID_HANDLE_VALUE;}
    bool openMapping(){
        if(view)return true;wchar_t publicPath[MAX_PATH]{};if(!GetEnvironmentVariableW(L"PUBLIC",publicPath,MAX_PATH))return false;
        std::wstring path=std::wstring(publicPath)+L"\\Documents\\WebCamKu\\frames.bin";
        file=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return false;
        mapping=CreateFileMappingW(file,nullptr,PAGE_READONLY,0,0,nullptr);if(!mapping){closeMapping();return false;}
        view=MapViewOfFile(mapping,FILE_MAP_READ,0,0,HeaderSize+FrameBytes);if(!view||*static_cast<DWORD*>(view)!=0x31464B57u){closeMapping();return false;}return true;
    }
    bool readLatest(){
        if(!openMapping())return false;auto header=static_cast<volatile LONG64*>(view);
        FILETIME now{};GetSystemTimeAsFileTime(&now);auto ticks=(static_cast<ULONGLONG>(now.dwHighDateTime)<<32)|now.dwLowDateTime;auto heartbeat=header[5];
        if(!heartbeat||ticks>static_cast<ULONGLONG>(heartbeat)+20'000'000ULL)return false;
        for(int attempt=0;attempt<4;attempt++){auto sequence=header[3];if(!sequence||(sequence&1)){SwitchToThread();continue;}MemoryBarrier();memcpy(pixels.data(),static_cast<BYTE*>(view)+HeaderSize,FrameBytes);MemoryBarrier();if(header[3]==sequence){hasFrame=true;return true;}}
        return false;
    }
    void run(){
        using clock=std::chrono::steady_clock;auto next=clock::now();
        while(running){readLatest();if(hasFrame&&outputVideo){obs_source_frame frame{};frame.data[0]=pixels.data();frame.linesize[0]=Stride;frame.width=Width;frame.height=Height;frame.format=VIDEO_FORMAT_BGRA;frame.full_range=true;frame.timestamp=static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(clock::now().time_since_epoch()).count());outputVideo(source,&frame);}next+=std::chrono::microseconds(33333);std::this_thread::sleep_until(next);if(clock::now()-next>std::chrono::milliseconds(100))next=clock::now();}
        if(outputVideo)outputVideo(source,nullptr);
    }
};

static const char* sourceName(void*){return "WebCamKu Source";}
static void* sourceCreate(obs_data_t*,obs_source_t* source){try{return new WebCamKuSource(source);}catch(...){return nullptr;}}
static void sourceDestroy(void* data){delete static_cast<WebCamKuSource*>(data);}
static uint32_t sourceWidth(void*){return Width;}
static uint32_t sourceHeight(void*){return Height;}

MODULE_EXPORT const char* obs_module_name(void){return "WebCamKu OBS Plugin";}
MODULE_EXPORT const char* obs_module_description(void){return "Low-latency direct WebCamKu frame source";}
MODULE_EXPORT bool obs_module_load(void){
    auto obs=GetModuleHandleW(L"obs.dll");if(!obs)return false;
    auto registerSource=reinterpret_cast<RegisterSourceFn>(GetProcAddress(obs,"obs_register_source_s"));
    outputVideo=reinterpret_cast<OutputVideoFn>(GetProcAddress(obs,"obs_source_output_video"));if(!registerSource||!outputVideo)return false;
    obs_source_info info{};info.id="webcamku_source";info.type=OBS_SOURCE_TYPE_INPUT;info.output_flags=OBS_SOURCE_ASYNC_VIDEO;info.get_name=sourceName;info.create=sourceCreate;info.destroy=sourceDestroy;info.get_width=sourceWidth;info.get_height=sourceHeight;registerSource(&info,sizeof(info));return true;
}

