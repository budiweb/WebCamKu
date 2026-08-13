package id.webcamku.app

import androidx.camera.core.CameraSelector
import androidx.lifecycle.ViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

enum class CameraFacing(val label: String, val lensFacing: Int) {
    Back("Back", CameraSelector.LENS_FACING_BACK),
    Front("Front", CameraSelector.LENS_FACING_FRONT),
}

data class CameraUiState(
    val facing: CameraFacing = CameraFacing.Back,
    val status: String = "Starting camera…",
    val isReady: Boolean = false,
    val isEncoding: Boolean = false,
    val encodedFrames: Int = 0,
    val keyFrames: Int = 0,
    val configFrames: Int = 0,
    val lastTimestampUs: Long = 0,
    val isServerRunning: Boolean = false,
    val streamedFrames: Int = 0,
    val streamedBytes: Long = 0,
    val droppedFrames: Int = 0,
)

class CameraViewModel : ViewModel() {
    private val mutableState = MutableStateFlow(CameraUiState())
    val state: StateFlow<CameraUiState> = mutableState.asStateFlow()

    fun switchCamera() {
        mutableState.update { current ->
            current.copy(
                facing = if (current.facing == CameraFacing.Back) CameraFacing.Front else CameraFacing.Back,
                status = "Switching camera…",
                isReady = false,
            )
        }
    }

    fun onCameraReady() {
        mutableState.update { it.copy(status = "Preview active", isReady = true) }
    }

    fun onCameraError(message: String) {
        mutableState.update { it.copy(status = message, isReady = false) }
    }

    fun beginEncoding() {
        mutableState.update {
            it.copy(status = "Starting 720p H.264 encoder…", isReady = false, isEncoding = true,
                encodedFrames = 0, keyFrames = 0, configFrames = 0, lastTimestampUs = 0)
        }
    }

    fun onEncoderStarted() {
        mutableState.update { it.copy(status = "Encoding 1280×720 at 30 FPS") }
    }

    fun onEncodedFrame(isKeyFrame: Boolean, isConfig: Boolean, timestampUs: Long) {
        mutableState.update {
            it.copy(
                encodedFrames = it.encodedFrames + 1,
                keyFrames = it.keyFrames + if (isKeyFrame) 1 else 0,
                configFrames = it.configFrames + if (isConfig) 1 else 0,
                lastTimestampUs = timestampUs,
            )
        }
    }

    fun endEncoding() {
        mutableState.update { it.copy(status = "Starting camera…", isEncoding = false, isReady = false) }
    }

    fun startServer() {
        mutableState.update { it.copy(isServerRunning = true, isReady = false, status = "Starting server…") }
    }

    fun stopServer() {
        mutableState.update { it.copy(isServerRunning = false, isReady = false, status = "Starting camera…") }
    }

    fun onServerStatus(status: String) {
        mutableState.update { it.copy(status = status) }
    }

    fun onServerStats(frames: Int, bytes: Long, dropped: Int) {
        mutableState.update { it.copy(streamedFrames = frames, streamedBytes = bytes, droppedFrames = dropped) }
    }
}
