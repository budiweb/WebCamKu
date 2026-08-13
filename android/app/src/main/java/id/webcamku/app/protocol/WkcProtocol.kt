package id.webcamku.app.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

object WkcProtocol {
    val magic = byteArrayOf('W'.code.toByte(), 'K'.code.toByte(), 'C'.code.toByte(), '1'.code.toByte())
    const val version: Byte = 1
    const val headerSize = 24
    const val maxPayloadSize = 16 * 1024 * 1024
}

enum class WkcMessageType(val value: Byte) {
    Hello(0x01),
    HelloAck(0x02),
    VideoConfig(0x03),
    VideoFrame(0x04),
    Command(0x05),
    CommandAck(0x06),
    Ping(0x07),
    Pong(0x08),
    Stats(0x09),
    Error(0x0A),
    StreamStart(0x0B),
    StreamStop(0x0C),
}

data class WkcPacket(
    val type: WkcMessageType,
    val flags: UShort = 0u,
    val timestampUs: ULong = 0u,
    val sequenceNumber: UInt,
    val payload: ByteArray = byteArrayOf(),
)

object WkcPacketWriter {
    fun encode(packet: WkcPacket): ByteArray {
        require(packet.payload.size <= WkcProtocol.maxPayloadSize) { "Payload exceeds WKC/1 limit" }
        return ByteBuffer.allocate(WkcProtocol.headerSize + packet.payload.size)
            .order(ByteOrder.BIG_ENDIAN)
            .put(WkcProtocol.magic)
            .put(WkcProtocol.version)
            .put(packet.type.value)
            .putShort(packet.flags.toShort())
            .putInt(packet.payload.size)
            .putLong(packet.timestampUs.toLong())
            .putInt(packet.sequenceNumber.toInt())
            .put(packet.payload)
            .array()
    }
}

object WkcPacketReader {
    fun read(input: java.io.InputStream): WkcPacket {
        val header = input.readExactly(WkcProtocol.headerSize)
        require(header.copyOfRange(0, 4).contentEquals(WkcProtocol.magic)) { "Invalid WKC/1 magic" }
        require(header[4] == WkcProtocol.version) { "Unsupported WKC version" }
        val buffer = ByteBuffer.wrap(header).order(ByteOrder.BIG_ENDIAN)
        val typeValue = header[5]
        val type = WkcMessageType.entries.firstOrNull { it.value == typeValue }
            ?: error("Unknown WKC/1 message type")
        val length = buffer.getInt(8)
        require(length in 0..WkcProtocol.maxPayloadSize) { "Invalid WKC/1 payload length" }
        return WkcPacket(
            type = type,
            flags = buffer.getShort(6).toUShort(),
            timestampUs = buffer.getLong(12).toULong(),
            sequenceNumber = buffer.getInt(20).toUInt(),
            payload = input.readExactly(length),
        )
    }

    private fun java.io.InputStream.readExactly(size: Int): ByteArray {
        val result = ByteArray(size)
        var offset = 0
        while (offset < size) {
            val read = read(result, offset, size - offset)
            if (read < 0) throw java.io.EOFException("Disconnected during WKC/1 packet")
            offset += read
        }
        return result
    }
}

object WkcMessages {
    fun hello(deviceName: String, sequenceNumber: UInt): WkcPacket = WkcPacket(
        type = WkcMessageType.Hello,
        sequenceNumber = sequenceNumber,
        payload = """{"deviceName":"${escape(deviceName)}","appVersion":"0.1.0","protocolVersion":1}"""
            .encodeToByteArray(),
    )

    fun helloAck(accepted: Boolean, sequenceNumber: UInt): WkcPacket = WkcPacket(
        type = WkcMessageType.HelloAck,
        sequenceNumber = sequenceNumber,
        payload = """{"accepted":$accepted,"protocolVersion":1}""".encodeToByteArray(),
    )

    fun ping(sequenceNumber: UInt, timestampUs: ULong): WkcPacket =
        WkcPacket(WkcMessageType.Ping, timestampUs = timestampUs, sequenceNumber = sequenceNumber)

    fun pong(sequenceNumber: UInt, timestampUs: ULong): WkcPacket =
        WkcPacket(WkcMessageType.Pong, timestampUs = timestampUs, sequenceNumber = sequenceNumber)

    private fun escape(value: String): String = buildString {
        value.forEach { character ->
            when (character) {
                '\\' -> append("\\\\")
                '"' -> append("\\\"")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                else -> append(character)
            }
        }
    }
}
