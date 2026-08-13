package id.webcamku.app.encoding

import android.annotation.SuppressLint
import android.content.Context
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.hardware.camera2.CaptureRequest
import android.os.Handler
import android.os.HandlerThread
import android.util.Range

class EncoderCameraSession(
    context: Context,
    private val encoder: H264Encoder,
    private val onStarted: () -> Unit,
    private val onError: (Throwable) -> Unit,
) : AutoCloseable {
    private val cameraManager = context.getSystemService(CameraManager::class.java)
    private val cameraThread = HandlerThread("WebCamKu-CameraEncoder").also { it.start() }
    private val handler = Handler(cameraThread.looper)
    private var camera: CameraDevice? = null
    private var session: CameraCaptureSession? = null

    @SuppressLint("MissingPermission")
    fun start() {
        encoder.start()
        // CameraX closes asynchronously after its preview use case is unbound.
        // Give the previous owner time to release the camera before opening Camera2.
        handler.postDelayed({ openBackCamera() }, CAMERA_RELEASE_DELAY_MS)
    }

    @SuppressLint("MissingPermission")
    private fun openBackCamera() {
        val cameraId = cameraManager.cameraIdList.first { id ->
            cameraManager.getCameraCharacteristics(id).get(CameraCharacteristics.LENS_FACING) ==
                CameraCharacteristics.LENS_FACING_BACK
        }
        cameraManager.openCamera(cameraId, object : CameraDevice.StateCallback() {
            override fun onOpened(device: CameraDevice) {
                camera = device
                createSession(device)
            }

            override fun onDisconnected(device: CameraDevice) {
                onError(IllegalStateException("Camera disconnected"))
                close()
            }

            override fun onError(device: CameraDevice, error: Int) {
                onError(IllegalStateException("Camera error $error"))
                close()
            }
        }, handler)
    }

    private fun createSession(device: CameraDevice) {
        val surface = checkNotNull(encoder.inputSurface)
        device.createCaptureSession(listOf(surface), object : CameraCaptureSession.StateCallback() {
            override fun onConfigured(captureSession: CameraCaptureSession) {
                session = captureSession
                runCatching {
                    val request = device.createCaptureRequest(CameraDevice.TEMPLATE_RECORD).apply {
                        addTarget(surface)
                        set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, Range(30, 30))
                        set(CaptureRequest.CONTROL_MODE, CaptureRequest.CONTROL_MODE_AUTO)
                    }.build()
                    captureSession.setRepeatingRequest(request, null, handler)
                }.onSuccess { onStarted() }.onFailure(onError)
            }

            override fun onConfigureFailed(captureSession: CameraCaptureSession) {
                onError(IllegalStateException("Encoder camera session configuration failed"))
            }
        }, handler)
    }

    @Synchronized
    override fun close() {
        handler.removeCallbacksAndMessages(null)
        runCatching { session?.stopRepeating() }
        session?.close()
        session = null
        camera?.close()
        camera = null
        encoder.stop()
        cameraThread.quitSafely()
    }

    private companion object {
        const val CAMERA_RELEASE_DELAY_MS = 1_500L
    }
}
