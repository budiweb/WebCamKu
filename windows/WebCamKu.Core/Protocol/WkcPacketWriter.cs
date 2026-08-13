using System.Buffers.Binary;

namespace WebCamKu.Core.Protocol;

public static class WkcPacketWriter
{
    public static byte[] Encode(WkcPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Payload.Length > WkcProtocol.MaximumPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(packet), "Payload exceeds WKC/1 limit.");

        var bytes = new byte[WkcProtocol.HeaderSize + packet.Payload.Length];
        WkcProtocol.Magic.CopyTo(bytes);
        bytes[4] = WkcProtocol.Version;
        bytes[5] = (byte)packet.Type;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), packet.Flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)packet.Payload.Length);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(12), packet.TimestampUs);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), packet.SequenceNumber);
        packet.Payload.CopyTo(bytes, WkcProtocol.HeaderSize);
        return bytes;
    }
}

public static class WkcProtocol
{
    public static ReadOnlySpan<byte> Magic => "WKC1"u8;
    public const byte Version = 1;
    public const int HeaderSize = 24;
    public const int MaximumPayloadSize = 16 * 1024 * 1024;
}
