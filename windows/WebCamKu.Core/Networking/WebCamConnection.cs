using System.Net.Sockets;
using System.Collections.Concurrent;
using WebCamKu.Core.Protocol;

namespace WebCamKu.Core.Networking;

public sealed record ConnectionStatistics(long FramesReceived, long BytesReceived, long ConfigPackets, long StatsPackets);
public enum WebCamConnectionState { Disconnected, Connecting, Handshaking, Streaming, Reconnecting, Error }

public static class ReconnectPolicy
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)];
    public static TimeSpan DelayForAttempt(int attempt) => Delays[Math.Clamp(attempt, 0, Delays.Length - 1)];
}

public sealed class WebCamConnection : IAsyncDisposable
{
    private readonly TimeSpan connectTimeout;
    private readonly TimeSpan inactivityTimeout;
    private readonly TcpClient client = new();
    private readonly WkcPacketReader reader = new();
    private NetworkStream? stream;
    private int sequence;
    private long commandId;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WkcMessages.CommandAcknowledgement>> pendingCommands = new();

    public event Action<string>? StatusChanged;
    public event Action<WebCamConnectionState>? StateChanged;
    public event Action<ConnectionStatistics>? StatisticsChanged;
    public event Action<WkcPacket>? EncodedPacketReceived;

    public WebCamConnection(TimeSpan? connectTimeout = null, TimeSpan? inactivityTimeout = null)
    {
        this.connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
        this.inactivityTimeout = inactivityTimeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<WkcMessages.CommandAcknowledgement> SendCommandAsync(
        string name, object? value = null, CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new InvalidOperationException("The phone is not connected.");
        var id = Interlocked.Increment(ref commandId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<WkcMessages.CommandAcknowledgement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingCommands.TryAdd(id, completion)) throw new InvalidOperationException("Could not register command.");
        try
        {
            await WriteAsync(WkcMessages.Command(id, name, value, NextSequence()), cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally { pendingCommands.TryRemove(id, out _); }
    }

    public async Task RunAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(client.Close);
        SetState(WebCamConnectionState.Connecting, $"Connecting to {host}:{port}");
        try { await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(connectTimeout, cancellationToken); }
        catch (TimeoutException) { throw new TimeoutException($"Connection to {host}:{port} timed out after {connectTimeout.TotalSeconds:0.#} seconds."); }
        client.NoDelay = true;
        stream = client.GetStream();
        SetState(WebCamConnectionState.Handshaking, "Handshaking");

        var hello = await ReadOneAsync(cancellationToken);
        if (hello.Type != WkcMessageType.Hello || !WkcMessages.TryReadHello(hello.Payload, out var helloJson))
            throw new WkcProtocolException("Android sent an invalid HELLO message.");
        helloJson!.Dispose();
        await WriteAsync(WkcMessages.HelloAck(true, NextSequence()), cancellationToken);
        await WriteAsync(new WkcPacket(WkcMessageType.StreamStart, 0, 0, NextSequence(), []), cancellationToken);
        SetState(WebCamConnectionState.Streaming, "Streaming");

        long frames = 0, bytes = 0, configs = 0, stats = 0;
        var receiveBuffer = new byte[64 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await ReadWithTimeoutAsync(receiveBuffer, $"No packets received from the phone for {inactivityTimeout.TotalSeconds:0.#} seconds.", cancellationToken);
            if (read == 0) throw new EndOfStreamException("Phone disconnected.");
            foreach (var packet in reader.Append(receiveBuffer.AsSpan(0, read)))
            {
                switch (packet.Type)
                {
                    case WkcMessageType.VideoConfig:
                        configs++;
                        bytes += packet.Payload.Length;
                        EncodedPacketReceived?.Invoke(packet);
                        break;
                    case WkcMessageType.VideoFrame:
                        frames++;
                        bytes += packet.Payload.Length;
                        EncodedPacketReceived?.Invoke(packet);
                        break;
                    case WkcMessageType.Stats:
                        stats++;
                        break;
                    case WkcMessageType.CommandAck:
                        if (WkcMessages.TryReadCommandAcknowledgement(packet.Payload, out var acknowledgement) &&
                            pendingCommands.TryRemove(acknowledgement!.CommandId, out var completion))
                            completion.TrySetResult(acknowledgement);
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
            var read = await ReadWithTimeoutAsync(buffer, $"Phone did not complete the handshake within {inactivityTimeout.TotalSeconds:0.#} seconds.", cancellationToken);
            if (read == 0) throw new EndOfStreamException("Phone disconnected during handshake.");
            var packets = reader.Append(buffer.AsSpan(0, read));
            if (packets.Count > 0) return packets[0];
        }
    }

    private async Task<int> ReadWithTimeoutAsync(Memory<byte> buffer, string message, CancellationToken cancellationToken)
    {
        try { return await stream!.ReadAsync(buffer, cancellationToken).AsTask().WaitAsync(inactivityTimeout, cancellationToken); }
        catch (TimeoutException) { throw new TimeoutException(message); }
    }

    private void SetState(WebCamConnectionState state, string status)
    {
        StateChanged?.Invoke(state);
        StatusChanged?.Invoke(status);
    }

    private uint NextSequence() => unchecked((uint)Interlocked.Increment(ref sequence));

    private async Task WriteAsync(WkcPacket packet, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken);
        try { await stream!.WriteAsync(WkcPacketWriter.Encode(packet), cancellationToken); }
        finally { writeGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (stream is not null)
        {
            runSilently(() => stream.Close());
            await stream.DisposeAsync();
        }
        client.Dispose();
        foreach (var pending in pendingCommands.Values)
            pending.TrySetException(new EndOfStreamException("Phone disconnected before command acknowledgement."));
        pendingCommands.Clear();
        writeGate.Dispose();
        StateChanged?.Invoke(WebCamConnectionState.Disconnected);
    }

    private static void runSilently(Action action)
    {
        try { action(); } catch { }
    }
}
