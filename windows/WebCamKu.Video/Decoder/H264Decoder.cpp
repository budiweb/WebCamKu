#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <wmcodecdsp.h>
#include <codecapi.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdint>
#include <memory>
#include <vector>

using Microsoft::WRL::ComPtr;
using FrameCallback = void(__stdcall*)(const std::uint8_t*, int, int, int, std::int64_t, void*);

namespace {
constexpr HRESULT NeedMoreInput = static_cast<HRESULT>(0xC00D6D72L);
constexpr HRESULT StreamChange = static_cast<HRESULT>(0xC00D6D61L);
constexpr HRESULT NotAccepting = static_cast<HRESULT>(0xC00D36B0L);

class Decoder final {
public:
    Decoder(FrameCallback callback, void* context) : callback_(callback), context_(context) {}
    ~Decoder() { shutdown(); }

    HRESULT initialize() {
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        comInitialized_ = SUCCEEDED(hr);
        if (hr == RPC_E_CHANGED_MODE) hr = S_OK;
        if (FAILED(hr)) return hr;
        if (FAILED(hr = MFStartup(MF_VERSION, MFSTARTUP_LITE))) return hr;
        mediaFoundationStarted_ = true;
        if (FAILED(hr = CoCreateInstance(CLSID_CMSH264DecoderMFT, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&decoder_)))) return hr;
        ComPtr<IMFAttributes> decoderAttributes;
        if (SUCCEEDED(decoder_->GetAttributes(&decoderAttributes))) decoderAttributes->SetUINT32(MF_LOW_LATENCY, TRUE);
        ComPtr<ICodecAPI> codecApi;
        if (SUCCEEDED(decoder_.As(&codecApi))) {
            VARIANT lowLatency{}; VariantInit(&lowLatency); lowLatency.vt=VT_BOOL; lowLatency.boolVal=VARIANT_TRUE;
            codecApi->SetValue(&CODECAPI_AVLowLatencyMode, &lowLatency); VariantClear(&lowLatency);
        }

        ComPtr<IMFMediaType> input;
        if (FAILED(hr = MFCreateMediaType(&input))) return hr;
        if (FAILED(hr = input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video))) return hr;
        if (FAILED(hr = input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264_ES))) return hr;
        if (FAILED(hr = MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, 1280, 720))) return hr;
        if (FAILED(hr = MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, 30, 1))) return hr;
        if (FAILED(hr = MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1))) return hr;
        if (FAILED(hr = input->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive))) return hr;
        if (FAILED(hr = decoder_->SetInputType(0, input.Get(), 0))) return hr;
        if (FAILED(hr = selectNv12Output())) return hr;
        decoder_->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
        decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
        return S_OK;
    }

    HRESULT push(const std::uint8_t* data, int length, std::int64_t timestampUs) {
        if (!data || length <= 0 || !decoder_) return E_INVALIDARG;
        ComPtr<IMFMediaBuffer> buffer;
        ComPtr<IMFSample> sample;
        HRESULT hr = MFCreateMemoryBuffer(static_cast<DWORD>(length), &buffer);
        if (FAILED(hr)) return hr;
        BYTE* destination = nullptr;
        if (FAILED(hr = buffer->Lock(&destination, nullptr, nullptr))) return hr;
        std::copy_n(data, length, destination);
        buffer->Unlock();
        buffer->SetCurrentLength(static_cast<DWORD>(length));
        if (FAILED(hr = MFCreateSample(&sample))) return hr;
        if (FAILED(hr = sample->AddBuffer(buffer.Get()))) return hr;
        sample->SetSampleTime(timestampUs * 10);
        sample->SetSampleDuration(10'000'000 / 30);

        hr = decoder_->ProcessInput(0, sample.Get(), 0);
        if (hr == NotAccepting) {
            drain();
            hr = decoder_->ProcessInput(0, sample.Get(), 0);
        }
        if (FAILED(hr)) return hr;
        return drain();
    }

    HRESULT reset() {
        if (!decoder_) return E_POINTER;
        HRESULT hr = decoder_->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
        if (FAILED(hr)) return hr;
        decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
        if (FAILED(hr = decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0))) return hr;
        return decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    }

private:
    HRESULT selectNv12Output() {
        for (DWORD index = 0;; ++index) {
            ComPtr<IMFMediaType> type;
            HRESULT hr = decoder_->GetOutputAvailableType(0, index, &type);
            if (FAILED(hr)) return hr;
            GUID subtype{};
            if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)) && subtype == MFVideoFormat_NV12) {
                UINT32 width = 0, height = 0;
                MFGetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, &width, &height);
                width_ = width ? static_cast<int>(width) : 1280;
                height_ = height ? static_cast<int>(height) : 720;
                return decoder_->SetOutputType(0, type.Get(), 0);
            }
        }
    }

    HRESULT drain() {
        for (;;) {
            MFT_OUTPUT_STREAM_INFO info{};
            HRESULT hr = decoder_->GetOutputStreamInfo(0, &info);
            if (FAILED(hr)) return hr;
            ComPtr<IMFSample> outputSample;
            ComPtr<IMFMediaBuffer> outputBuffer;
            if (!(info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)) {
                if (FAILED(hr = MFCreateSample(&outputSample))) return hr;
                const DWORD size = std::max<DWORD>(info.cbSize, static_cast<DWORD>(width_ * height_ * 3 / 2));
                if (FAILED(hr = MFCreateMemoryBuffer(size, &outputBuffer))) return hr;
                if (FAILED(hr = outputSample->AddBuffer(outputBuffer.Get()))) return hr;
            }
            MFT_OUTPUT_DATA_BUFFER output{};
            output.pSample = outputSample.Get();
            DWORD status = 0;
            hr = decoder_->ProcessOutput(0, 1, &output, &status);
            if (output.pEvents) output.pEvents->Release();
            if (hr == NeedMoreInput) return S_OK;
            if (hr == StreamChange) {
                if (FAILED(hr = selectNv12Output())) return hr;
                continue;
            }
            if (FAILED(hr)) return hr;
            ComPtr<IMFSample> produced = output.pSample;
            if (!produced) continue;
            ComPtr<IMFMediaBuffer> contiguous;
            if (FAILED(hr = produced->ConvertToContiguousBuffer(&contiguous))) return hr;
            BYTE* nv12 = nullptr;
            DWORD currentLength = 0;
            if (FAILED(hr = contiguous->Lock(&nv12, nullptr, &currentLength))) return hr;
            convertNv12ToBgra(nv12, currentLength);
            contiguous->Unlock();
            LONGLONG time = 0;
            produced->GetSampleTime(&time);
            if (callback_) callback_(bgra_.data(), static_cast<int>(bgra_.size()), width_, height_, time / 10, context_);
        }
    }

    void convertNv12ToBgra(const BYTE* input, DWORD length) {
        const auto ySize = width_ * height_;
        if (length < static_cast<DWORD>(ySize + ySize / 2)) return;
        bgra_.resize(static_cast<std::size_t>(width_) * height_ * 4);
        const BYTE* uv = input + ySize;
        for (int y = 0; y < height_; ++y) {
            for (int x = 0; x < width_; ++x) {
                const int yy = std::max(0, static_cast<int>(input[y * width_ + x]) - 16);
                const int uvIndex = (y / 2) * width_ + (x & ~1);
                const int u = static_cast<int>(uv[uvIndex]) - 128;
                const int v = static_cast<int>(uv[uvIndex + 1]) - 128;
                const auto clamp = [](int value) { return static_cast<BYTE>(std::clamp(value, 0, 255)); };
                const auto index = (static_cast<std::size_t>(y) * width_ + x) * 4;
                bgra_[index] = clamp((298 * yy + 516 * u + 128) >> 8);
                bgra_[index + 1] = clamp((298 * yy - 100 * u - 208 * v + 128) >> 8);
                bgra_[index + 2] = clamp((298 * yy + 409 * v + 128) >> 8);
                bgra_[index + 3] = 255;
            }
        }
    }

    void shutdown() {
        if (decoder_) {
            decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
            decoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
            decoder_.Reset();
        }
        if (mediaFoundationStarted_) MFShutdown();
        if (comInitialized_) CoUninitialize();
    }

    ComPtr<IMFTransform> decoder_;
    FrameCallback callback_{};
    void* context_{};
    std::vector<std::uint8_t> bgra_;
    int width_{1280};
    int height_{720};
    bool mediaFoundationStarted_{};
    bool comInitialized_{};
};
}

extern "C" __declspec(dllexport) void* __cdecl WkcDecoderCreate(FrameCallback callback, void* context, HRESULT* result) {
    auto decoder = std::make_unique<Decoder>(callback, context);
    const HRESULT hr = decoder->initialize();
    if (result) *result = hr;
    return SUCCEEDED(hr) ? decoder.release() : nullptr;
}

extern "C" __declspec(dllexport) HRESULT __cdecl WkcDecoderPush(void* handle, const std::uint8_t* data, int length, std::int64_t timestampUs) {
    return handle ? static_cast<Decoder*>(handle)->push(data, length, timestampUs) : E_POINTER;
}

extern "C" __declspec(dllexport) HRESULT __cdecl WkcDecoderReset(void* handle) {
    return handle ? static_cast<Decoder*>(handle)->reset() : E_POINTER;
}

extern "C" __declspec(dllexport) void __cdecl WkcDecoderDestroy(void* handle) {
    delete static_cast<Decoder*>(handle);
}
