package id.webcamku.app.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class WkcPacketWriterTest {
    @Test
    fun `writer emits the fixed big endian header`() {
        val bytes = WkcPacketWriter.encode(
            WkcPacket(WkcMessageType.Ping, 0x0102u, 0x0102030405060708u, 0x11223344u, byteArrayOf(5, 6)),
        )
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.BIG_ENDIAN)

        assertArrayEquals(WkcProtocol.magic, bytes.copyOfRange(0, 4))
        assertEquals(1, buffer.get(4).toInt())
        assertEquals(0x07, buffer.get(5).toInt())
        assertEquals(0x0102, buffer.getShort(6).toInt())
        assertEquals(2, buffer.getInt(8))
        assertEquals(0x0102030405060708L, buffer.getLong(12))
        assertEquals(0x11223344, buffer.getInt(20))
    }

    @Test
    fun `hello escapes untrusted device names`() {
        val json = WkcMessages.hello("Phone \"A\"", 1u).payload.decodeToString()
        assertTrue(json.contains("Phone \\\"A\\\""))
    }
}
