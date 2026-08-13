using System.Net.Sockets;
using WebCamKu.Core.Protocol;

namespace WebCamKu.Core.Networking;

public sealed record ConnectionStatistics(long FramesReceived, long BytesReceived, long ConfigPackets, long StatsPackets);

public sealed class WebCamConnection : IAsyncDisposable
{
    private readonly TcpClient client = new();
    private readonly WkcPacketReader reader = new();
    private NetworkStream? stream;
    private uint sequence = 1;

    public event Action<string>? StatusChanged;
    public event Action<ConnectionStatistics>? StatisticsChanged;

    public async Task RunAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(client.Close);
        StatusChanged?.Invoke("Connecting");
        await client.ConnectAsync(host, port, cancellationToken);
        client.NoDelay = true;
        stream = client.GetStream();
        StatusChanged?.Invoke("Handshaking");

        var hello = await ReadOneAsync(cancellationToken);
        if (hello.Type != WkcMessageType.Hello || !WkcMessages.TryReadHello(hello.Payload, out var helloJson))
            throw new WkcProtocolException("Android sent an invalid HELLO message.");
        helloJson!.Dispose();
        await WriteAsync(WkcMessages.HelloAck(true, sequence++), cancellationToken);
        await WriteAsync(new WkcPacket(WkcMessageType.StreamStart, 0, 0, sequence++, []), cancellationToken);
        StatusChanged?.Invoke("Streaming (encoded H.264 receive only)");

        long frames = 0, bytes = 0, configs = 0, stats = 0;
        var receiveBuffer = new byte[64 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(receiveBuffer, cancellationToken);
            if (read == 0) throw new EndOfStreamException("Phone disconnected.");
            foreach (var packet in reader.Append(receiveBuffer.AsSpan(0, read)))
            {
                switch (packet.Type)
                {
                    case WkcMessageType.VideoConfig:
                        configs++;
                        bytes += packet.Payload.Length;
                        break;
                    case WkcMessageType.VideoFrame:
                        frames++;
                        bytes += packet.Payload.Length;
                        break;
                    case WkcMessageType.Stats:
                        stats++;
                        break;
                }
            }
            StatisticsChanged?.Invoke(new(frames, bytes, configs, stats));
        }
    }

    private async Task<WkcPacket> ReadOneAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream!.ReadAsync(buffer, cancellationToken);
            if (read == 0) throw new EndOfStreamException("Phone disconnected during handshake.");
            var packets = reader.Append(buffer.AsSpan(0, read));
            if (packets.Count > 0) return packets[0];
        }
    }

    private Task WriteAsync(WkcPacket packet, CancellationToken cancellationToken) =>
        stream!.WriteAsync(WkcPacketWriter.Encode(packet), cancellationToken).AsTask();

    public async ValueTask DisposeAsync()
    {
        if (stream is not null)
        {
            runSilently(() => stream.Close());
            await stream.DisposeAsync();
        }
        client.Dispose();
    }

    private static void runSilently(Action action)
    {
        try { action(); } catch { }
    }
}
