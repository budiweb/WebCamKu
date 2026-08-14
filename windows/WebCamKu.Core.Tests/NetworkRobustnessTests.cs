using System.Net;
using System.Net.Sockets;
using WebCamKu.Core.Networking;
using WebCamKu.Core.Protocol;
using Xunit;

namespace WebCamKu.Core.Tests;

public sealed class NetworkRobustnessTests
{
    [Fact]
    public void ReconnectBackoffIsBounded()
    {
        Assert.Equal(new[] { 1d, 2d, 3d, 5d, 5d, 5d },
            Enumerable.Range(0, 6).Select(attempt => ReconnectPolicy.DelayForAttempt(attempt).TotalSeconds));
    }

    [Fact]
    public async Task HandshakeTimesOutCleanly()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            await Task.Delay(500);
        });
        await using var connection = new WebCamConnection(
            connectTimeout: TimeSpan.FromSeconds(1), inactivityTimeout: TimeSpan.FromMilliseconds(100));

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            connection.RunAsync(IPAddress.Loopback.ToString(), port, CancellationToken.None));
        Assert.Contains("handshake", error.Message, StringComparison.OrdinalIgnoreCase);
        await server;
    }

    [Fact]
    public async Task DisconnectDuringStreamingIsReported()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            var hello = new WkcPacket(WkcMessageType.Hello, 0, 0, 1,
                "{\"deviceName\":\"Synthetic Phone\",\"protocolVersion\":1}"u8.ToArray());
            await stream.WriteAsync(WkcPacketWriter.Encode(hello));
            await Task.Delay(100);
        });
        await using var connection = new WebCamConnection(inactivityTimeout: TimeSpan.FromSeconds(1));

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            connection.RunAsync(IPAddress.Loopback.ToString(), port, CancellationToken.None));
        Assert.True(error is EndOfStreamException or IOException);
        await server;
    }
}
