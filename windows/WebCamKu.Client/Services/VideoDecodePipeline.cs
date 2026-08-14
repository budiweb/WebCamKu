using WebCamKu.Core.Protocol;

namespace WebCamKu.Client.Services;

internal sealed class VideoDecodePipeline : IAsyncDisposable
{
    // A webcam should always decode the newest access unit; queued old frames become visible latency.
    private readonly object gate = new();
    private readonly SemaphoreSlim available = new(0);
    private readonly MediaFoundationDecoder decoder = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private bool completed;
    private long droppedFrames;
    private byte[]? codecConfiguration;
    private bool configurationPending;
    private WkcPacket? latestFrame;
    public long DroppedFrames => Interlocked.Read(ref droppedFrames);
    public long DecoderErrors { get; private set; }
    public event Action<byte[], int, int, long>? FrameDecoded;

    public VideoDecodePipeline()
    {
        decoder.FrameDecoded += (data, width, height, timestamp) => FrameDecoded?.Invoke(data, width, height, timestamp);
        worker = Task.Run(DecodeLoopAsync);
    }

    public void Submit(WkcPacket packet)
    {
        lock (gate)
        {
            if (completed) return;
            var wasEmpty = !configurationPending && latestFrame is null;
            if (packet.Type == WkcMessageType.VideoConfig)
            {
                codecConfiguration = packet.Payload.ToArray();
                configurationPending = true;
            }
            else
            {
                if (latestFrame is not null) Interlocked.Increment(ref droppedFrames);
                latestFrame = packet;
            }
            if (wasEmpty) available.Release();
        }
    }

    private async Task DecodeLoopAsync()
    {
        try
        {
            while (true)
            {
                await available.WaitAsync(cancellation.Token);
                byte[]? configuration;
                WkcPacket? packet;
                lock (gate)
                {
                    configuration = configurationPending ? codecConfiguration : null;
                    configurationPending = false;
                    packet = latestFrame;
                    latestFrame = null;
                    if (completed && configuration is null && packet is null) break;
                }
                try
                {
                    if (configuration is not null)
                        decoder.Push(configuration, packet is null ? 0 : (long)packet.TimestampUs);
                    if (packet is not null)
                        decoder.Push(packet.Payload, (long)packet.TimestampUs);
                }
                catch
                {
                    DecoderErrors++;
                    try
                    {
                        decoder.Reset();
                        if (codecConfiguration is not null)
                            decoder.Push(codecConfiguration, packet is null ? 0 : (long)packet.TimestampUs);
                    }
                    catch { DecoderErrors++; }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            completed = true;
            latestFrame = null;
            configurationPending = false;
        }
        cancellation.Cancel();
        await worker;
        decoder.Dispose();
        available.Dispose();
        cancellation.Dispose();
    }
}
