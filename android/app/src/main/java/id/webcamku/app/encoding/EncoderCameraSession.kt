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
import android.os.Build
import android.util.Range
import android.graphics.Rect
import android.view.Surface

class EncoderCameraSession(
    context: Context,
    private val encoder: H264Encoder,
    private val onStarted: () -> Unit,
    private val onError: (Throwable) -> Unit,
    private val previewSurface: Surface? = null,
) : AutoCloseable {
    private val cameraManager = context.getSystemService(CameraManager::class.java)
    private val cameraThread = HandlerThread("WebCamKu-CameraEncoder").also { it.start() }
    private val handler = Handler(cameraThread.looper)
    private var camera: CameraDevice? = null
    private var session: CameraCaptureSession? = null
    private var requestBuilder: CaptureRequest.Builder? = null
    private var cameraId: String? = null
    private var facing = CameraCharacteristics.LENS_FACING_BACK
    private var torchEnabled = false
    private var zoomRatio = 1f
    private var autoFocus = true
    private var manualFocus = 0f

    @SuppressLint("MissingPermission")
    fun start() {
        encoder.start()
        // CameraX closes asynchronously after its preview use case is unbound.
        // Give the previous owner time to release the camera before opening Camera2.
        handler.postDelayed({ openCamera() }, CAMERA_RELEASE_DELAY_MS)
    }

    @SuppressLint("MissingPermission")
    private fun openCamera() {
        val selectedId = cameraManager.cameraIdList.firstOrNull { id ->
            cameraManager.getCameraCharacteristics(id).get(CameraCharacteristics.LENS_FACING) ==
                facing
        } ?: return onError(UnsupportedOperationException("Requested camera is not available"))
        cameraId = selectedId
        cameraManager.openCamera(selectedId, object : CameraDevice.StateCallback() {
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
        val targets = listOfNotNull(surface, previewSurface?.takeIf { it.isValid })
        device.createCaptureSession(targets, object : CameraCaptureSession.StateCallback() {
            override fun onConfigured(captureSession: CameraCaptureSession) {
                session = captureSession
                runCatching {
                    // PREVIEW favors the shortest camera pipeline; the target remains the encoder surface.
                    requestBuilder = device.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW).apply {
                        addTarget(surface)
                        previewSurface?.takeIf { it.isValid }?.let(::addTarget)
                        set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, Range(30, 30))
                        set(CaptureRequest.CONTROL_MODE, CaptureRequest.CONTROL_MODE_AUTO)
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) set(CaptureRequest.CONTROL_ENABLE_ZSL, false)
                        set(CaptureRequest.NOISE_REDUCTION_MODE, CaptureRequest.NOISE_REDUCTION_MODE_FAST)
                        set(CaptureRequest.EDGE_MODE, CaptureRequest.EDGE_MODE_FAST)
                    }
                    applyControls()
                }.onSuccess { onStarted() }.onFailure(onError)
            }

            override fun onConfigureFailed(captureSession: CameraCaptureSession) {
                onError(IllegalStateException("Encoder camera session configuration failed"))
            }
        }, handler)
    }

    @Synchronized
    fun switchCamera(): String {
        val target = if (facing == CameraCharacteristics.LENS_FACING_BACK)
            CameraCharacteristics.LENS_FACING_FRONT else CameraCharacteristics.LENS_FACING_BACK
        check(cameraManager.cameraIdList.any {
            cameraManager.getCameraCharacteristics(it).get(CameraCharacteristics.LENS_FACING) == target
        }) { "Requested camera is not available" }
        facing = target
        torchEnabled = false
        zoomRatio = 1f
        handler.post {
            runCatching { session?.stopRepeating() }
            session?.close()
            session = null
            camera?.close()
            camera = null
            requestBuilder = null
            openCamera()
        }
        return if (target == CameraCharacteristics.LENS_FACING_FRONT) "front" else "back"
    }

    @Synchronized
    fun setTorch(enabled: Boolean): Boolean {
        val id = cameraId ?: error("Camera is not ready")
        val supported = cameraManager.getCameraCharacteristics(id)
            .get(CameraCharacteristics.FLASH_INFO_AVAILABLE) == true
        check(supported) { "Torch is not supported by this camera" }
        torchEnabled = enabled
        handler.post { applyControls() }
        return enabled
    }

    @Synchronized
    fun setZoom(requestedRatio: Float): Float {
        require(requestedRatio.isFinite() && requestedRatio >= 1f) { "Zoom must be a finite ratio of at least 1" }
        val id = cameraId ?: error("Camera is not ready")
        val characteristics = cameraManager.getCameraCharacteristics(id)
        val maximum = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R)
            characteristics.get(CameraCharacteristics.CONTROL_ZOOM_RATIO_RANGE)?.upper
        else characteristics.get(CameraCharacteristics.SCALER_AVAILABLE_MAX_DIGITAL_ZOOM)
        val supportedMaximum = maximum ?: 1f
        check(supportedMaximum > 1f) { "Digital zoom is not supported by this camera" }
        zoomRatio = requestedRatio.coerceAtMost(supportedMaximum)
        handler.post { applyControls() }
        return zoomRatio
    }

    @Synchronized
    fun setAutoFocus(enabled: Boolean): Boolean {
        autoFocus = enabled
        handler.post { applyControls() }
        return enabled
    }

    @Synchronized
    fun setManualFocus(normalizedDistance: Float): Float {
        require(normalizedDistance.isFinite() && normalizedDistance in 0f..1f) {
            "Manual focus must be between 0 and 1"
        }
        val id = cameraId ?: error("Camera is not ready")
        val minimumDistance = cameraManager.getCameraCharacteristics(id)
            .get(CameraCharacteristics.LENS_INFO_MINIMUM_FOCUS_DISTANCE) ?: 0f
        check(minimumDistance > 0f) { "Manual focus is not supported by this camera" }
        autoFocus = false
        manualFocus = normalizedDistance
        handler.post { applyControls() }
        return manualFocus
    }

    fun requestKeyFrame() = encoder.requestKeyFrame()

    private fun applyControls() {
        val captureSession = session ?: return
        val builder = requestBuilder ?: return
        builder.set(CaptureRequest.FLASH_MODE, if (torchEnabled) CaptureRequest.FLASH_MODE_TORCH else CaptureRequest.FLASH_MODE_OFF)
        if (autoFocus) {
            builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_VIDEO)
        } else {
            val id = cameraId ?: return
            val minimumDistance = cameraManager.getCameraCharacteristics(id)
                .get(CameraCharacteristics.LENS_INFO_MINIMUM_FOCUS_DISTANCE) ?: 0f
            check(minimumDistance > 0f) { "Manual focus is not supported by this camera" }
            builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_OFF)
            builder.set(CaptureRequest.LENS_FOCUS_DISTANCE, manualFocus * minimumDistance)
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            builder.set(CaptureRequest.CONTROL_ZOOM_RATIO, zoomRatio)
        } else {
            val id = cameraId ?: return
            val active = cameraManager.getCameraCharacteristics(id)
                .get(CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE) ?: return
            val width = (active.width() / zoomRatio).toInt()
            val height = (active.height() / zoomRatio).toInt()
            val left = active.left + (active.width() - width) / 2
            val top = active.top + (active.height() - height) / 2
            builder.set(CaptureRequest.SCALER_CROP_REGION, Rect(left, top, left + width, top + height))
        }
        captureSession.setRepeatingRequest(builder.build(), null, handler)
    }

    @Synchronized
    override fun close() {
        handler.removeCallbacksAndMessages(null)
        runCatching { session?.stopRepeating() }
        session?.close()
        session = null
        requestBuilder = null
        camera?.close()
        camera = null
        encoder.stop()
        cameraThread.quitSafely()
    }

    private companion object {
        const val CAMERA_RELEASE_DELAY_MS = 1_500L
    }
}
