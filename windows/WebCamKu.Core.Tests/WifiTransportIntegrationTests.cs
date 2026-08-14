using WebCamKu.Core.Networking;
using Xunit;

namespace WebCamKu.Core.Tests;

public sealed class WifiTransportIntegrationTests
{
    [Fact]
    [Trait("Category", "PhysicalDevice")]
    public async Task RapidConnectDisconnectDoesNotBreakServer()
    {
        var host = Environment.GetEnvironmentVariable("WEBCAMKU_TEST_HOST");
        if (string.IsNullOrWhiteSpace(host)) return;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var connection = new WebCamConnection();
            var streaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.StateChanged += state =>
            {
                if (state == WebCamConnectionState.Streaming) streaming.TrySetResult();
            };
            var run = connection.RunAsync(host, 4747, cancellation.Token);
            await streaming.Task.WaitAsync(cancellation.Token);
            cancellation.Cancel();
            try { await run; } catch (Exception) when (cancellation.IsCancellationRequested) { }
            await Task.Delay(150);
        }

        using var finalCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var finalConnection = new WebCamConnection();
        long frames = 0;
        finalConnection.StatisticsChanged += value => Interlocked.Exchange(ref frames, value.FramesReceived);
        var finalRun = finalConnection.RunAsync(host, 4747, finalCancellation.Token);
        await WaitForFramesAsync(() => Interlocked.Read(ref frames), 30, finalCancellation.Token);
        finalCancellation.Cancel();
        try { await finalRun; } catch (Exception) when (finalCancellation.IsCancellationRequested) { }
    }

    [Fact]
    [Trait("Category", "PhysicalDevice")]
    public async Task RemoteControlsAreAcknowledgedAndStreamResumes()
    {
        var host = Environment.GetEnvironmentVariable("WEBCAMKU_TEST_HOST");
        if (string.IsNullOrWhiteSpace(host)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var connection = new WebCamConnection();
        long frames = 0;
        connection.StatisticsChanged += value => Interlocked.Exchange(ref frames, value.FramesReceived);
        var receiveTask = connection.RunAsync(host, 4747, timeout.Token);
        await WaitForFramesAsync(() => Interlocked.Read(ref frames), 30, timeout.Token);

        var zoom = await connection.SendCommandAsync("zoom", 2.0, timeout.Token);
        Assert.True(zoom.Success, zoom.Error);
        var afterZoom = Interlocked.Read(ref frames);
        await WaitForFramesAsync(() => Interlocked.Read(ref frames), afterZoom + 15, timeout.Token);

        var torchOn = await connection.SendCommandAsync("torch", true, timeout.Token);
        Assert.True(torchOn.Success, torchOn.Error);
        Assert.True((await connection.SendCommandAsync("torch", false, timeout.Token)).Success);

        var switched = await connection.SendCommandAsync("switchCamera", null, timeout.Token);
        Assert.True(switched.Success, switched.Error);
        var beforeResume = Interlocked.Read(ref frames);
        await WaitForFramesAsync(() => Interlocked.Read(ref frames), beforeResume + 15, timeout.Token);

        var unsupportedTorch = await connection.SendCommandAsync("torch", true, timeout.Token);
        Assert.False(unsupportedTorch.Success);
        Assert.False(string.IsNullOrWhiteSpace(unsupportedTorch.Error));
        Assert.True((await connection.SendCommandAsync("switchCamera", null, timeout.Token)).Success);
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(async () => await receiveTask);
    }

    private static async Task WaitForFramesAsync(Func<long> current, long target, CancellationToken cancellationToken)
    {
        while (current() < target) await Task.Delay(50, cancellationToken);
    }

    [Fact]
    [Trait("Category", "PhysicalDevice")]
    public async Task ReceivesContinuousH264FromConfiguredPhone()
    {
        var host = Environment.GetEnvironmentVariable("WEBCAMKU_TEST_HOST");
        if (string.IsNullOrWhiteSpace(host)) return;

        var duration = int.TryParse(Environment.GetEnvironmentVariable("WEBCAMKU_TEST_DURATION_SECONDS"), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(30);
        var isDurationRun = duration >= TimeSpan.FromMinutes(1);
        using var timeout = new CancellationTokenSource();
        await using var connection = new WebCamConnection();
        ConnectionStatistics latest = new(0, 0, 0, 0);
        connection.StatisticsChanged += stats =>
        {
            latest = stats;
            if (!isDurationRun && stats.FramesReceived >= 90 && stats.ConfigPackets >= 1) timeout.Cancel();
        };
        var receiveTask = connection.RunAsync(host, 4747, timeout.Token);
        try
        {
            if (isDurationRun)
            {
                await Task.Delay(duration);
                timeout.Cancel();
            }
            await receiveTask;
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            // Closing the socket guarantees deterministic completion even when reads are continuously ready.
        }
        Assert.True(latest.ConfigPackets >= 1, "Expected H.264 codec configuration.");
        Assert.True(latest.FramesReceived >= 90, "Expected at least three seconds of encoded video.");
        Assert.True(latest.BytesReceived > 0, "Expected non-empty H.264 payloads.");
    }
}
