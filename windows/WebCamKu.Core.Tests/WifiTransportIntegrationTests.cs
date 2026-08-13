using WebCamKu.Core.Networking;
using Xunit;

namespace WebCamKu.Core.Tests;

public sealed class WifiTransportIntegrationTests
{
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
