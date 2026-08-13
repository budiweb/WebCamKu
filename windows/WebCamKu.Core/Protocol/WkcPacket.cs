namespace WebCamKu.Core.Protocol;

public enum WkcMessageType : byte
{
    Hello = 0x01,
    HelloAck = 0x02,
    VideoConfig = 0x03,
    VideoFrame = 0x04,
    Command = 0x05,
    CommandAck = 0x06,
    Ping = 0x07,
    Pong = 0x08,
    Stats = 0x09,
    Error = 0x0A,
    StreamStart = 0x0B,
    StreamStop = 0x0C,
}

public sealed record WkcPacket(
    WkcMessageType Type,
    ushort Flags,
    ulong TimestampUs,
    uint SequenceNumber,
    byte[] Payload);

public sealed class WkcProtocolException(string message) : Exception(message);
