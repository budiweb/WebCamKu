using System.Text.Json;

namespace WebCamKu.Core.Protocol;

public static class WkcMessages
{
    public static WkcPacket HelloAck(bool accepted, uint sequenceNumber) => JsonPacket(
        WkcMessageType.HelloAck,
        sequenceNumber,
        new { accepted, protocolVersion = 1 });

    public static WkcPacket Ping(uint sequenceNumber, ulong timestampUs) =>
        new(WkcMessageType.Ping, 0, timestampUs, sequenceNumber, []);

    public static WkcPacket Pong(uint sequenceNumber, ulong timestampUs) =>
        new(WkcMessageType.Pong, 0, timestampUs, sequenceNumber, []);

    private static WkcPacket JsonPacket(WkcMessageType type, uint sequenceNumber, object value) =>
        new(type, 0, 0, sequenceNumber, JsonSerializer.SerializeToUtf8Bytes(value));

    public static bool TryReadHello(ReadOnlySpan<byte> payload, out JsonDocument? document)
    {
        document = null;
        try
        {
            var parsed = JsonDocument.Parse(payload.ToArray());
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("deviceName", out var name) || name.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("protocolVersion", out var version) || version.GetInt32() != 1)
            {
                parsed.Dispose();
                return false;
            }
            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
