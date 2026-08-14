using WebCamKu.Client.Services;
using WebCamKu.Core.Networking;
using Xunit;

namespace WebCamKu.Client.Tests;

public sealed class VideoDecodeIntegrationTests
{
    [Fact]
    [Trait("Category", "PhysicalDevice")]
    public async Task DecoderRecoversAfterRejectedAccessUnit()
    {
        var host = Environment.GetEnvironmentVariable("WEBCAMKU_TEST_HOST");
        if (string.IsNullOrWhiteSpace(host)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = new WebCamConnection();
        await using var pipeline = new VideoDecodePipeline();
        var decoded = 0;
        pipeline.FrameDecoded += (_, _, _, _) => Interlocked.Increment(ref decoded);
        connection.EncodedPacketReceived += pipeline.Submit;
        var run = connection.RunAsync(host, 4747, timeout.Token);
        while (Volatile.Read(ref decoded) < 30) await Task.Delay(25, timeout.Token);
        pipeline.Submit(new(WebCamKu.Core.Protocol.WkcMessageType.VideoFrame, 0, 0, 0, []));
        while (pipeline.DecoderErrors == 0) await Task.Delay(25, timeout.Token);
        var recoveredAt = Volatile.Read(ref decoded);
        while (Volatile.Read(ref decoded) < recoveredAt + 30) await Task.Delay(25, timeout.Token);
        timeout.Cancel();
        try { await run; } catch (Exception) when (timeout.IsCancellationRequested) { }
        Assert.True(pipeline.DecoderErrors >= 1);
    }

    [Fact]
    [Trait("Category", "PhysicalDevice")]
    public async Task DecodesLiveH264FramesWithMediaFoundation()
    {
        var host = Environment.GetEnvironmentVariable("WEBCAMKU_TEST_HOST");
        if (string.IsNullOrWhiteSpace(host)) return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = new WebCamConnection();
        await using var pipeline = new VideoDecodePipeline();
        var decoded = 0;
        var width = 0;
        var height = 0;
        pipeline.FrameDecoded += (_, frameWidth, frameHeight, _) =>
        {
            width = frameWidth;
            height = frameHeight;
            if (Interlocked.Increment(ref decoded) >= 60) timeout.Cancel();
        };
        connection.EncodedPacketReceived += pipeline.Submit;
        try { await connection.RunAsync(host, 4747, timeout.Token); }
        catch (Exception) when (timeout.IsCancellationRequested) { }

        Assert.True(decoded >= 60, $"Expected 60 decoded frames; received {decoded}, errors {pipeline.DecoderErrors}.");
        Assert.Equal(1280, width);
        Assert.Equal(720, height);
        Assert.Equal(0, pipeline.DecoderErrors);
    }
}
