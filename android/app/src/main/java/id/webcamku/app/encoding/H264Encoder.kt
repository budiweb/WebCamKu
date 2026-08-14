package id.webcamku.app.encoding

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.Handler
import android.os.HandlerThread
import android.os.Bundle
import android.view.Surface

data class EncodedFrame(
    val data: ByteArray,
    val presentationTimeUs: Long,
    val isCodecConfig: Boolean,
    val isKeyFrame: Boolean,
)

class H264Encoder(
    private val onFrame: (EncodedFrame) -> Unit,
    private val onError: (Throwable) -> Unit,
) : AutoCloseable {
    private var codec: MediaCodec? = null
    private var callbackThread: HandlerThread? = null
    var inputSurface: Surface? = null
        private set

    @Synchronized
    fun start() {
        check(codec == null) { "Encoder is already running" }
        val thread = HandlerThread("WebCamKu-H264").also { it.start() }
        val encoder = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, WIDTH, HEIGHT).apply {
            setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
            setInteger(MediaFormat.KEY_BIT_RATE, BIT_RATE)
            setInteger(MediaFormat.KEY_FRAME_RATE, FRAME_RATE)
            setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, I_FRAME_INTERVAL_SECONDS)
            setInteger("max-bframes", 0)
            setInteger("latency", 0)
            setInteger(MediaFormat.KEY_PRIORITY, 0)
            setInteger(MediaFormat.KEY_OPERATING_RATE, FRAME_RATE)
        }
        encoder.setCallback(object : MediaCodec.Callback() {
            override fun onInputBufferAvailable(codec: MediaCodec, index: Int) = Unit

            override fun onOutputBufferAvailable(codec: MediaCodec, index: Int, info: MediaCodec.BufferInfo) {
                try {
                    val output = codec.getOutputBuffer(index)
                    if (output != null && info.size > 0) {
                        output.position(info.offset)
                        output.limit(info.offset + info.size)
                        val bytes = ByteArray(info.size)
                        output.get(bytes)
                        onFrame(
                            EncodedFrame(
                                data = bytes,
                                presentationTimeUs = info.presentationTimeUs,
                                isCodecConfig = info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0,
                                isKeyFrame = info.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME != 0,
                            ),
                        )
                    }
                } finally {
                    runCatching { codec.releaseOutputBuffer(index, false) }
                }
            }

            override fun onError(codec: MediaCodec, error: MediaCodec.CodecException) = onError(error)
            override fun onOutputFormatChanged(codec: MediaCodec, format: MediaFormat) = Unit
        }, Handler(thread.looper))
        try {
            encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            inputSurface = encoder.createInputSurface()
            encoder.start()
            callbackThread = thread
            codec = encoder
        } catch (error: Throwable) {
            runCatching { encoder.release() }
            thread.quitSafely()
            inputSurface = null
            throw error
        }
    }

    @Synchronized
    fun stop() {
        val encoder = codec ?: return
        codec = null
        inputSurface = null
        runCatching { encoder.signalEndOfInputStream() }
        runCatching { encoder.stop() }
        runCatching { encoder.release() }
        callbackThread?.quitSafely()
        callbackThread = null
    }

    @Synchronized
    fun requestKeyFrame() {
        codec?.setParameters(Bundle().apply {
            putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
        })
    }

    override fun close() = stop()

    companion object {
        const val WIDTH = 1280
        const val HEIGHT = 720
        const val FRAME_RATE = 30
        const val BIT_RATE = 4_000_000
        const val I_FRAME_INTERVAL_SECONDS = 1
    }
}
