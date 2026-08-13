package id.webcamku.app.network

import android.content.Context
import id.webcamku.app.encoding.EncoderCameraSession
import id.webcamku.app.encoding.H264Encoder

class StreamingSession(
    context: Context,
    onStatus: (String) -> Unit,
    onStats: (ServerStats) -> Unit,
) : AutoCloseable {
    private val applicationContext = context.applicationContext
    private var cameraSession: EncoderCameraSession? = null
    private val server = StreamServer(
        onStreamStart = { startEncoder(onStatus) },
        onStreamStop = { stopEncoder() },
        onStatus = onStatus,
        onStats = onStats,
    )

    fun start() = server.start()

    @Synchronized
    private fun startEncoder(onStatus: (String) -> Unit) {
        if (cameraSession != null) return
        val encoder = H264Encoder(server::offer) { onStatus("Encoder error: ${it.message}") }
        cameraSession = EncoderCameraSession(
            applicationContext,
            encoder,
            { onStatus("Streaming 1280×720 H.264") },
            { onStatus("Camera error: ${it.message}") },
        ).also { it.start() }
    }

    @Synchronized
    private fun stopEncoder() {
        cameraSession?.close()
        cameraSession = null
    }

    override fun close() {
        server.close()
        stopEncoder()
    }
}
