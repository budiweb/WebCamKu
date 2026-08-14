using System.Buffers.Binary;
using WebCamKu.Core.Protocol;
using Xunit;

namespace WebCamKu.Core.Tests;

public sealed class WkcPacketTests
{
    private static readonly WkcPacket Packet = new(WkcMessageType.Hello, 2, 123, 7, "hello"u8.ToArray());

    [Fact]
    public void HeaderRoundTripsBigEndianValues()
    {
        var bytes = WkcPacketWriter.Encode(Packet);
        var parsed = Assert.Single(new WkcPacketReader().Append(bytes));
        Assert.Equal(Packet.Type, parsed.Type);
        Assert.Equal(Packet.Flags, parsed.Flags);
        Assert.Equal(Packet.TimestampUs, parsed.TimestampUs);
        Assert.Equal(Packet.SequenceNumber, parsed.SequenceNumber);
        Assert.True(Packet.Payload.SequenceEqual(parsed.Payload));
    }

    [Fact]
    public void SplitPacketWaitsForRemainingBytes()
    {
        var bytes = WkcPacketWriter.Encode(Packet);
        var reader = new WkcPacketReader();
        Assert.Empty(reader.Append(bytes.AsSpan(0, 11)));
        Assert.Empty(reader.Append(bytes.AsSpan(11, 14)));
        Assert.Single(reader.Append(bytes.AsSpan(25)));
    }

    [Fact]
    public void ConcatenatedPacketsAreBothReturned()
    {
        var bytes = WkcPacketWriter.Encode(Packet).Concat(WkcPacketWriter.Encode(WkcMessages.Ping(8, 456))).ToArray();
        Assert.Equal(2, new WkcPacketReader().Append(bytes).Count);
    }

    [Fact]
    public void InvalidMagicIsRejected()
    {
        var bytes = WkcPacketWriter.Encode(Packet);
        bytes[0] = 0;
        Assert.Throws<WkcProtocolException>(() => new WkcPacketReader().Append(bytes));
    }

    [Fact]
    public void UnsupportedVersionIsRejected()
    {
        var bytes = WkcPacketWriter.Encode(Packet);
        bytes[4] = 2;
        Assert.Throws<WkcProtocolException>(() => new WkcPacketReader().Append(bytes));
    }

    [Fact]
    public void ExcessivePayloadLengthIsRejectedBeforeAllocation()
    {
        var bytes = WkcPacketWriter.Encode(Packet);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), WkcProtocol.MaximumPayloadSize + 1u);
        Assert.Throws<WkcProtocolException>(() => new WkcPacketReader().Append(bytes));
    }

    [Fact]
    public void HelloJsonRequiresDeviceNameAndSupportedVersion()
    {
        Assert.True(WkcMessages.TryReadHello("{\"deviceName\":\"Phone\",\"protocolVersion\":1}"u8, out var valid));
        valid!.Dispose();
        Assert.False(WkcMessages.TryReadHello("{\"protocolVersion\":1}"u8, out _));
        Assert.False(WkcMessages.TryReadHello("not json"u8, out _));
    }

    [Fact]
    public void SequenceNumberIsPreservedAtUnsignedBoundary()
    {
        var packet = WkcMessages.Pong(uint.MaxValue, ulong.MaxValue);
        var parsed = Assert.Single(new WkcPacketReader().Append(WkcPacketWriter.Encode(packet)));
        Assert.Equal(uint.MaxValue, parsed.SequenceNumber);
        Assert.Equal(ulong.MaxValue, parsed.TimestampUs);
    }

    [Fact]
    public void CommandAndAcknowledgementUseCorrelatedJson()
    {
        var command = WkcMessages.Command("zoom-1", "zoom", 2.5, 12);
        using var json = System.Text.Json.JsonDocument.Parse(command.Payload);
        Assert.Equal("zoom-1", json.RootElement.GetProperty("commandId").GetString());
        Assert.Equal(2.5, json.RootElement.GetProperty("value").GetDouble());

        Assert.True(WkcMessages.TryReadCommandAcknowledgement(
            "{\"commandId\":\"zoom-1\",\"success\":false,\"error\":\"unsupported\"}"u8,
            out var acknowledgement));
        Assert.Equal("unsupported", acknowledgement!.Error);
        Assert.False(WkcMessages.TryReadCommandAcknowledgement("{\"success\":true}"u8, out _));
    }
}
