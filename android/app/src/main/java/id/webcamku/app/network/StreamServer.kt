package id.webcamku.app.network

import android.os.Build
import id.webcamku.app.encoding.EncodedFrame
import id.webcamku.app.protocol.WkcMessageType
import id.webcamku.app.protocol.WkcMessages
import id.webcamku.app.protocol.WkcPacket
import id.webcamku.app.protocol.WkcPacketReader
import id.webcamku.app.protocol.WkcPacketWriter
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread

data class ServerStats(val framesSent: Int, val bytesSent: Long, val droppedFrames: Int)

class StreamServer(
    private val port: Int = 4747,
    private val onStreamStart: () -> Unit,
    private val onStreamStop: () -> Unit,
    private val onStatus: (String) -> Unit,
    private val onStats: (ServerStats) -> Unit,
) : AutoCloseable {
    private val running = AtomicBoolean(false)
    private val sequence = AtomicInteger(1)
    private val queue = ArrayBlockingQueue<EncodedFrame>(4)
    private var serverSocket: ServerSocket? = null
    private var client: Socket? = null
    private var worker: Thread? = null
    private var framesSent = 0
    private var bytesSent = 0L
    private var droppedFrames = 0

    fun start() {
        check(running.compareAndSet(false, true)) { "Server is already running" }
        worker = thread(name = "WebCamKu-StreamServer") { runServer() }
    }

    fun offer(frame: EncodedFrame) {
        if (!running.get()) return
        if (!queue.offer(frame)) {
            queue.poll()
            if (!queue.offer(frame)) return
            droppedFrames++
        }
    }

    private fun runServer() {
        try {
            ServerSocket(port).use { listener ->
                serverSocket = listener
                listener.reuseAddress = true
                onStatus("Listening on port $port")
                while (running.get()) {
                    try {
                        listener.accept().use { socket ->
                            client = socket
                            socket.tcpNoDelay = true
                            socket.soTimeout = 15_000
                            handleClient(socket)
                        }
                    } catch (error: Exception) {
                        if (running.get()) onStatus("Client disconnected; listening on port $port")
                    } finally {
                        client = null
                        queue.clear()
                        onStreamStop()
                    }
                }
            }
        } catch (error: Exception) {
            if (running.get()) onStatus("Server error: ${error.message}")
        } finally {
            running.set(false)
            onStreamStop()
        }
    }

    private fun handleClient(socket: Socket) {
        val input = socket.getInputStream()
        val output = socket.getOutputStream()
        write(output, WkcMessages.hello(Build.MODEL.ifBlank { "Android Phone" }, nextSequence()))
        val ack = WkcPacketReader.read(input)
        require(ack.type == WkcMessageType.HelloAck && ack.payload.decodeToString().contains("\"accepted\":true")) {
            "Client rejected WKC/1 handshake"
        }
        val start = WkcPacketReader.read(input)
        require(start.type == WkcMessageType.StreamStart) { "Expected STREAM_START" }
        socket.soTimeout = 0
        onStatus("Client connected; starting encoder")
        onStreamStart()

        while (running.get() && !socket.isClosed) {
            val frame = queue.take()
            val flags = (if (frame.isKeyFrame) 0x0001 else 0) or
                (if (frame.isCodecConfig) 0x0002 else 0)
            val type = if (frame.isCodecConfig) WkcMessageType.VideoConfig else WkcMessageType.VideoFrame
            write(output, WkcPacket(type, flags.toUShort(), frame.presentationTimeUs.toULong(), nextSequence(), frame.data))
            if (!frame.isCodecConfig) framesSent++
            bytesSent += frame.data.size
            if (framesSent > 0 && framesSent % 30 == 0) {
                val stats = ServerStats(framesSent, bytesSent, droppedFrames)
                onStats(stats)
                val json = """{"framesSent":$framesSent,"bytesSent":$bytesSent,"droppedFrames":$droppedFrames,"queueDepth":${queue.size}}"""
                write(output, WkcPacket(WkcMessageType.Stats, sequenceNumber = nextSequence(), payload = json.encodeToByteArray()))
            }
        }
    }

    private fun write(output: java.io.OutputStream, packet: WkcPacket) {
        output.write(WkcPacketWriter.encode(packet))
        output.flush()
    }

    private fun nextSequence() = sequence.getAndIncrement().toUInt()

    override fun close() {
        running.set(false)
        runCatching { client?.close() }
        runCatching { serverSocket?.close() }
        worker?.interrupt()
        worker = null
        queue.clear()
    }
}
