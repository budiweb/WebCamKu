using System.Runtime.InteropServices;

namespace WebCamKu.Client.Services;

internal sealed unsafe partial class MediaFoundationDecoder : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate void FrameCallback(byte* data, int length, int width, int height, long timestampUs, nint context);

    private readonly FrameCallback callback;
    private nint handle;
    private byte[]? reusableFrame;
    public event Action<byte[], int, int, long>? FrameDecoded;

    public MediaFoundationDecoder()
    {
        callback = OnFrame;
        handle = Native.WkcDecoderCreate(callback, 0, out var result);
        if (handle == 0) Marshal.ThrowExceptionForHR(result);
    }

    public unsafe void Push(byte[] data, long timestampUs)
    {
        fixed (byte* pointer = data)
        {
            var result = Native.WkcDecoderPush(handle, pointer, data.Length, timestampUs);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }
    }

    public void Reset()
    {
        var result = Native.WkcDecoderReset(handle);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private unsafe void OnFrame(byte* data, int length, int width, int height, long timestampUs, nint context)
    {
        if (reusableFrame is null || reusableFrame.Length != length)
            reusableFrame = GC.AllocateUninitializedArray<byte>(length);
        Marshal.Copy((nint)data, reusableFrame, 0, length);
        FrameDecoded?.Invoke(reusableFrame, width, height, timestampUs);
    }

    public void Dispose()
    {
        var decoder = Interlocked.Exchange(ref handle, 0);
        if (decoder != 0) Native.WkcDecoderDestroy(decoder);
        GC.KeepAlive(callback);
    }

    private static partial class Native
    {
        [LibraryImport("WebCamKu.Video.dll")]
        internal static partial nint WkcDecoderCreate(FrameCallback callback, nint context, out int result);

        [LibraryImport("WebCamKu.Video.dll")]
        internal static unsafe partial int WkcDecoderPush(nint handle, byte* data, int length, long timestampUs);

        [LibraryImport("WebCamKu.Video.dll")]
        internal static partial int WkcDecoderReset(nint handle);

        [LibraryImport("WebCamKu.Video.dll")]
        internal static partial void WkcDecoderDestroy(nint handle);
    }
}
