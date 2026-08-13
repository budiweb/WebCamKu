package id.webcamku.app

import android.content.Context
import androidx.camera.core.CameraSelector
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.common.util.concurrent.ListenableFuture
import java.util.concurrent.Executor

internal class CameraManager(
    private val context: Context,
    private val executor: Executor,
) {
    private val providerFuture: ListenableFuture<ProcessCameraProvider> =
        ProcessCameraProvider.getInstance(context)

    fun bind(
        previewView: PreviewView,
        lifecycleOwner: androidx.lifecycle.LifecycleOwner,
        lensFacing: Int,
        onReady: () -> Unit,
        onError: (String) -> Unit,
    ) {
        providerFuture.addListener({
            runCatching {
                val provider = providerFuture.get()
                val selector = CameraSelector.Builder().requireLensFacing(lensFacing).build()
                check(provider.hasCamera(selector)) { "Selected camera is not available" }
                val preview = Preview.Builder().build().also {
                    it.surfaceProvider = previewView.surfaceProvider
                }
                provider.unbindAll()
                provider.bindToLifecycle(lifecycleOwner, selector, preview)
            }.onSuccess {
                onReady()
            }.onFailure { error ->
                onError(error.message ?: "Camera preview could not be started")
            }
        }, executor)
    }

    fun unbind() {
        if (providerFuture.isDone) runCatching { providerFuture.get().unbindAll() }
    }
}

@Composable
fun CameraPreview(
    lensFacing: Int,
    onCameraReady: () -> Unit,
    onCameraError: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val manager = remember(context) {
        CameraManager(context.applicationContext, ContextCompatExecutor(context))
    }
    val previewView = remember(context) {
        PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER }
    }

    AndroidView(factory = { previewView }, modifier = modifier)

    DisposableEffect(lifecycleOwner, lensFacing) {
        manager.bind(previewView, lifecycleOwner, lensFacing, onCameraReady, onCameraError)
        onDispose { manager.unbind() }
    }
}

private class ContextCompatExecutor(context: Context) : Executor {
    private val delegate = ContextCompat.getMainExecutor(context)
    override fun execute(command: Runnable) = delegate.execute(command)
}
