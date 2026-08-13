using System.Buffers;
using System.Buffers.Binary;

namespace WebCamKu.Core.Protocol;

public sealed class WkcPacketReader
{
    private byte[] buffer = new byte[4096];
    private int count;

    public IReadOnlyList<WkcPacket> Append(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(count + data.Length);
        data.CopyTo(buffer.AsSpan(count));
        count += data.Length;

        var packets = new List<WkcPacket>();
        var consumed = 0;
        while (count - consumed >= WkcProtocol.HeaderSize)
        {
            var header = buffer.AsSpan(consumed, WkcProtocol.HeaderSize);
            ValidateHeader(header);
            var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (payloadLength > WkcProtocol.MaximumPayloadSize)
                throw new WkcProtocolException("Payload length exceeds WKC/1 limit.");

            var packetLength = checked(WkcProtocol.HeaderSize + (int)payloadLength);
            if (count - consumed < packetLength) break;

            var type = (WkcMessageType)header[5];
            if (!Enum.IsDefined(type)) throw new WkcProtocolException("Unknown WKC/1 message type.");
            packets.Add(new WkcPacket(
                type,
                BinaryPrimitives.ReadUInt16BigEndian(header[6..]),
                BinaryPrimitives.ReadUInt64BigEndian(header[12..]),
                BinaryPrimitives.ReadUInt32BigEndian(header[20..]),
                buffer.AsSpan(consumed + WkcProtocol.HeaderSize, (int)payloadLength).ToArray()));
            consumed += packetLength;
        }

        if (consumed > 0)
        {
            buffer.AsSpan(consumed, count - consumed).CopyTo(buffer);
            count -= consumed;
        }
        return packets;
    }

    private static void ValidateHeader(ReadOnlySpan<byte> header)
    {
        if (!header[..4].SequenceEqual(WkcProtocol.Magic))
            throw new WkcProtocolException("Invalid WKC/1 magic.");
        if (header[4] != WkcProtocol.Version)
            throw new WkcProtocolException("Unsupported WKC protocol version.");
    }

    private void EnsureCapacity(int required)
    {
        if (required <= buffer.Length) return;
        if (required > WkcProtocol.HeaderSize + WkcProtocol.MaximumPayloadSize)
            throw new WkcProtocolException("Buffered data exceeds WKC/1 limit.");
        Array.Resize(ref buffer, Math.Max(required, buffer.Length * 2));
    }
}
