package id.webcamku.app.network

import android.content.Context
import id.webcamku.app.encoding.EncoderCameraSession
import id.webcamku.app.encoding.H264Encoder
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import android.view.Surface

class StreamingSession(
    context: Context,
    private val onStatus: (String) -> Unit,
    onStats: (ServerStats) -> Unit,
    private val previewSurface: Surface? = null,
) : AutoCloseable {
    private val applicationContext = context.applicationContext
    private var cameraSession: EncoderCameraSession? = null
    private val recoveryExecutor = Executors.newSingleThreadScheduledExecutor { runnable ->
        Thread(runnable, "WebCamKu-EncoderRecovery")
    }
    private val recoveryPending = AtomicBoolean(false)
    @Volatile private var streamActive = false
    private val server = StreamServer(
        onStreamStart = {
            startEncoder(onStatus)
            cameraSession?.requestKeyFrame()
        },
        onStreamStop = {
            onStatus("Server ready - waiting for Windows")
        },
        onCommand = ::handleCommand,
        onStatus = onStatus,
        onStats = onStats,
    )

    fun start() {
        streamActive = true
        server.start()
        startEncoder(onStatus)
    }

    fun setAutoFocus(enabled: Boolean) = cameraSession?.setAutoFocus(enabled)
        ?: error("Camera stream is not ready")

    fun setManualFocus(distance: Float) = cameraSession?.setManualFocus(distance)
        ?: error("Camera stream is not ready")

    fun switchCamera(): String = cameraSession?.switchCamera()
        ?: error("Camera stream is not ready")

    @Synchronized
    private fun startEncoder(onStatus: (String) -> Unit) {
        if (cameraSession != null) return
        val encoder = H264Encoder(server::offer) { scheduleEncoderRecovery(onStatus, it) }
        cameraSession = EncoderCameraSession(
            applicationContext,
            encoder,
            { onStatus("Streaming 1280×720 H.264") },
            { scheduleEncoderRecovery(onStatus, it) },
            previewSurface,
        ).also { it.start() }
    }

    private fun scheduleEncoderRecovery(onStatus: (String) -> Unit, error: Throwable) {
        if (!streamActive || !recoveryPending.compareAndSet(false, true)) return
        onStatus("Video pipeline error: ${error.message}; restarting in 1 second")
        recoveryExecutor.schedule({
            try {
                if (!streamActive) return@schedule
                stopEncoder()
                startEncoder(onStatus)
                onStatus("Video pipeline restarted")
            } catch (restartError: Throwable) {
                onStatus("Video restart failed: ${restartError.message}")
                if (streamActive) {
                    recoveryPending.set(false)
                    scheduleEncoderRecovery(onStatus, restartError)
                    return@schedule
                }
            } finally {
                recoveryPending.set(false)
            }
        }, 1, TimeUnit.SECONDS)
    }

    @Synchronized
    private fun handleCommand(command: CameraCommand): CommandResult = runCatching {
        val session = cameraSession ?: error("Camera stream is not ready")
        when (command.name) {
            "switchCamera" -> CommandResult.ok(mapOf("facing" to session.switchCamera()))
            "torch" -> {
                val enabled = command.booleanValue ?: error("Torch value must be boolean")
                CommandResult.ok(mapOf("torch" to session.setTorch(enabled)))
            }
            "zoom" -> {
                val ratio = command.numberValue?.toFloat() ?: error("Zoom value must be numeric")
                CommandResult.ok(mapOf("zoom" to session.setZoom(ratio)))
            }
            else -> error("Unknown command: ${command.name}")
        }
    }.getOrElse { CommandResult.error(it.message ?: "Command failed") }

    @Synchronized
    private fun stopEncoder() {
        cameraSession?.close()
        cameraSession = null
    }

    override fun close() {
        streamActive = false
        server.close()
        stopEncoder()
        recoveryExecutor.shutdownNow()
    }
}
