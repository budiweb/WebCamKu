using System.Text.Json;

namespace WebCamKu.Core.Protocol;

public static class WkcMessages
{
    public sealed record CommandAcknowledgement(string CommandId, bool Success, string? Error);

    public static WkcPacket HelloAck(bool accepted, uint sequenceNumber) => JsonPacket(
        WkcMessageType.HelloAck,
        sequenceNumber,
        new { accepted, protocolVersion = 1 });

    public static WkcPacket Ping(uint sequenceNumber, ulong timestampUs) =>
        new(WkcMessageType.Ping, 0, timestampUs, sequenceNumber, []);

    public static WkcPacket Pong(uint sequenceNumber, ulong timestampUs) =>
        new(WkcMessageType.Pong, 0, timestampUs, sequenceNumber, []);

    public static WkcPacket Command(string commandId, string name, object? value, uint sequenceNumber) =>
        JsonPacket(WkcMessageType.Command, sequenceNumber, new { commandId, name, value });

    public static bool TryReadCommandAcknowledgement(ReadOnlySpan<byte> payload, out CommandAcknowledgement? acknowledgement)
    {
        acknowledgement = null;
        try
        {
            using var parsed = JsonDocument.Parse(payload.ToArray());
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("commandId", out var id) || id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) ||
                !root.TryGetProperty("success", out var success) ||
                (success.ValueKind != JsonValueKind.True && success.ValueKind != JsonValueKind.False)) return false;
            var error = root.TryGetProperty("error", out var errorValue) && errorValue.ValueKind == JsonValueKind.String
                ? errorValue.GetString() : null;
            acknowledgement = new(id.GetString()!, success.GetBoolean(), error);
            return true;
        }
        catch (JsonException) { return false; }
    }

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
