using System.IO.MemoryMappedFiles;
using System.IO;

namespace WebCamKu.Client.Services;

internal sealed class SharedFrameProvider : IDisposable
{
    internal const int Width = 1280;
    internal const int Height = 720;
    internal const int Stride = Width * 4;
    private const int HeaderSize = 64;
    private const long Capacity = HeaderSize + (long)Stride * Height;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private long sequence;

    public SharedFrameProvider()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "WebCamKu");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "frames.bin");
        using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            file.SetLength(Capacity);
        mapping = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, Capacity, MemoryMappedFileAccess.ReadWrite);
        view = mapping.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
        view.Write(0, 0x31464B57u); // WKF1
        view.Write(4, Width);
        view.Write(8, Height);
        view.Write(12, Stride);
        view.Write(16, HeaderSize);
        view.Write(24, 0L);
        view.Flush();
    }

    public void Publish(byte[] bgra, int width, int height, long timestampUs)
    {
        if (width != Width || height != Height || bgra.Length < Stride * Height) return;
        var writing = Interlocked.Add(ref sequence, 2) - 1;
        view.Write(24, writing);
        view.Write(32, timestampUs);
        view.Write(40, DateTime.UtcNow.Ticks);
        view.WriteArray(HeaderSize, bgra, 0, Stride * Height);
        Thread.MemoryBarrier();
        view.Write(24, writing + 1);
    }

    public void Dispose()
    {
        view.Dispose();
        mapping.Dispose();
    }
}
