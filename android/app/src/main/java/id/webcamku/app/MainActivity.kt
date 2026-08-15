package id.webcamku.app

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import android.view.Surface
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import id.webcamku.app.encoding.EncodedFrame
import id.webcamku.app.encoding.EncoderCameraSession
import id.webcamku.app.encoding.H264Encoder
import id.webcamku.app.network.StreamingSession
import java.net.Inet4Address
import java.net.NetworkInterface

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { WebCamKuApp() }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WebCamKuApp(cameraViewModel: CameraViewModel = viewModel()) {
    val context = LocalContext.current
    var hasCameraPermission by remember {
        mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
    }
    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        hasCameraPermission = it
    }
    LaunchedEffect(Unit) {
        if (!hasCameraPermission) permissionLauncher.launch(Manifest.permission.CAMERA)
    }

    val state by cameraViewModel.state.collectAsStateWithLifecycle()
    var previewSurface by remember { mutableStateOf<Surface?>(null) }
    var streamingSession by remember { mutableStateOf<StreamingSession?>(null) }
    var isDimmed by remember { mutableStateOf(false) }
    var autoFocus by remember { mutableStateOf(true) }
    var focusDistance by remember { mutableStateOf(0f) }

    DisposableEffect(isDimmed, state.isServerRunning) {
        val window = (context as? ComponentActivity)?.window
        if (state.isServerRunning) window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window?.attributes = window?.attributes?.apply {
            screenBrightness = if (isDimmed && state.isServerRunning) 0.03f
            else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
        }
        onDispose {
            window?.attributes = window?.attributes?.apply {
                screenBrightness = WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
            }
            window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    if (state.isEncoding) EncoderSession(cameraViewModel)
    previewSurface?.takeIf { state.isServerRunning }?.let { surface ->
        ServerSession(cameraViewModel, surface) { streamingSession = it }
    }

    MaterialTheme {
        Scaffold(topBar = { TopAppBar(title = { Text("WebCamKu") }) }) { padding ->
            BoxWithConstraints(Modifier.fillMaxSize().padding(padding).padding(12.dp)) {
                val landscape = maxWidth > maxHeight
                val preview: @Composable (Modifier) -> Unit = { modifier ->
                    when {
                        hasCameraPermission && state.isServerRunning -> StreamingCameraPreview(
                            onSurfaceChanged = { previewSurface = it }, modifier = modifier,
                        )
                        hasCameraPermission && !state.isEncoding -> CameraPreview(
                            lensFacing = state.facing.lensFacing,
                            onCameraReady = cameraViewModel::onCameraReady,
                            onCameraError = cameraViewModel::onCameraError,
                            modifier = modifier,
                        )
                        else -> PermissionRequired(
                            onRequestPermission = { permissionLauncher.launch(Manifest.permission.CAMERA) },
                            modifier = modifier,
                        )
                    }
                }
                val panel: @Composable (Modifier) -> Unit = { modifier ->
                    CameraControlPanel(
                        state = state,
                        hasCameraPermission = hasCameraPermission,
                        autoFocus = autoFocus,
                        focusDistance = focusDistance,
                        isDimmed = isDimmed,
                        modifier = modifier,
                        onSwitchCamera = cameraViewModel::switchCamera,
                        onSwitchStreamingCamera = {
                            runCatching { streamingSession?.switchCamera() ?: error("Camera stream is not ready") }
                                .onSuccess(cameraViewModel::onStreamingCameraSwitched)
                                .onFailure { cameraViewModel.onServerStatus(it.message ?: "Could not switch camera") }
                        },
                        onBeginEncoding = cameraViewModel::beginEncoding,
                        onStartServer = cameraViewModel::startServer,
                        onStopServer = { isDimmed = false; cameraViewModel.stopServer() },
                        onEndEncoding = cameraViewModel::endEncoding,
                        onAutoFocus = {
                            autoFocus = true
                            runCatching { streamingSession?.setAutoFocus(true) ?: error("Camera stream is not ready") }
                                .onFailure { cameraViewModel.onServerStatus(it.message ?: "Autofocus failed") }
                        },
                        onToggleDim = { isDimmed = !isDimmed },
                        onFocusChanged = { focusDistance = it },
                        onManualFocus = {
                            autoFocus = false
                            runCatching { streamingSession?.setManualFocus(focusDistance) ?: error("Camera stream is not ready") }
                                .onFailure { cameraViewModel.onServerStatus(it.message ?: "Manual focus unsupported") }
                        },
                    )
                }
                if (landscape) {
                    Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        preview(Modifier.weight(1.45f).fillMaxSize())
                        panel(Modifier.weight(1f).fillMaxSize())
                    }
                } else {
                    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        preview(Modifier.fillMaxWidth().aspectRatio(16f / 9f))
                        panel(Modifier.fillMaxWidth().weight(1f))
                    }
                }
            }
        }
    }
}

@Composable
private fun CameraControlPanel(
    state: CameraUiState,
    hasCameraPermission: Boolean,
    autoFocus: Boolean,
    focusDistance: Float,
    isDimmed: Boolean,
    modifier: Modifier,
    onSwitchCamera: () -> Unit,
    onSwitchStreamingCamera: () -> Unit,
    onBeginEncoding: () -> Unit,
    onStartServer: () -> Unit,
    onStopServer: () -> Unit,
    onEndEncoding: () -> Unit,
    onAutoFocus: () -> Unit,
    onToggleDim: () -> Unit,
    onFocusChanged: (Float) -> Unit,
    onManualFocus: () -> Unit,
) {
    val phoneIpAddress = remember(state.isServerRunning) {
        if (state.isServerRunning) findLocalIpv4Address() else null
    }
    Column(modifier, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(state.status, style = MaterialTheme.typography.titleSmall, maxLines = 2)
        Text("${state.facing.label} camera  •  1280×720 / 30 FPS", style = MaterialTheme.typography.bodySmall)
        when {
            state.isServerRunning -> {
                Text(
                    phoneIpAddress?.let { "Phone IP: $it  •  Port 4747" }
                        ?: "Phone IP unavailable  •  Port 4747",
                    style = MaterialTheme.typography.bodyMedium,
                )
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        Button(onClick = onSwitchStreamingCamera, modifier = Modifier.fillMaxWidth()) {
                            Text("Switch Camera")
                        }
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            Button(onClick = onAutoFocus, modifier = Modifier.weight(1f)) { Text("Auto Focus") }
                            OutlinedButton(onClick = onToggleDim, modifier = Modifier.weight(1f)) {
                                Text(if (isDimmed) "Restore" else "Dim")
                            }
                        }
                        Text(if (autoFocus) "Focus: Auto" else "Focus: Manual", style = MaterialTheme.typography.bodySmall)
                        Slider(value = focusDistance, onValueChange = onFocusChanged, onValueChangeFinished = onManualFocus)
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Text("Far", style = MaterialTheme.typography.labelSmall)
                            Text("Near", style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
                Spacer(Modifier.weight(1f))
                Button(onClick = onStopServer, modifier = Modifier.fillMaxWidth()) { Text("Stop Server") }
            }
            state.isEncoding -> {
                Text("${state.encodedFrames} frames  •  ${state.keyFrames} keyframes", style = MaterialTheme.typography.bodySmall)
                Spacer(Modifier.weight(1f))
                Button(onClick = onEndEncoding, modifier = Modifier.fillMaxWidth()) { Text("Stop Encoder Test") }
            }
            else -> {
                Spacer(Modifier.weight(1f))
                Button(onClick = onSwitchCamera, enabled = hasCameraPermission && state.isReady, modifier = Modifier.fillMaxWidth()) {
                    Text("Switch Camera")
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onBeginEncoding, enabled = hasCameraPermission && state.isReady, modifier = Modifier.weight(1f)) {
                        Text("Test Encoder")
                    }
                    Button(onClick = onStartServer, enabled = hasCameraPermission && state.isReady, modifier = Modifier.weight(1f)) {
                        Text("Start Server")
                    }
                }
            }
        }
    }
}

private fun findLocalIpv4Address(): String? = runCatching {
    NetworkInterface.getNetworkInterfaces().toList()
        .filter { it.isUp && !it.isLoopback }
        .sortedByDescending { it.name.equals("wlan0", ignoreCase = true) }
        .flatMap { it.inetAddresses.toList() }
        .filterIsInstance<Inet4Address>()
        .firstOrNull { !it.isLoopbackAddress && it.isSiteLocalAddress }
        ?.hostAddress
}.getOrNull()

@Composable
private fun ServerSession(viewModel: CameraViewModel, previewSurface: Surface, onSessionChanged: (StreamingSession?) -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(context, lifecycleOwner, previewSurface) {
        val session = StreamingSession(
            context,
            viewModel::onServerStatus,
            { stats -> viewModel.onServerStats(stats.framesSent, stats.bytesSent, stats.droppedFrames) },
            previewSurface,
        )
        onSessionChanged(session)
        runCatching { session.start() }.onFailure { viewModel.onServerStatus("Server could not start: ${it.message}") }
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) { session.close(); viewModel.stopServer() }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
            session.close()
            onSessionChanged(null)
        }
    }
}

@Composable
private fun StreamingCameraPreview(onSurfaceChanged: (Surface?) -> Unit, modifier: Modifier = Modifier) {
    AndroidView(
        modifier = modifier,
        factory = { context ->
            SurfaceView(context).apply {
                holder.addCallback(object : SurfaceHolder.Callback {
                    override fun surfaceCreated(holder: SurfaceHolder) = onSurfaceChanged(holder.surface)
                    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) = onSurfaceChanged(holder.surface)
                    override fun surfaceDestroyed(holder: SurfaceHolder) = onSurfaceChanged(null)
                })
            }
        },
    )
}

@Composable
private fun EncoderSession(viewModel: CameraViewModel) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(context, lifecycleOwner) {
        val encoder = H264Encoder(
            onFrame = { frame: EncodedFrame -> viewModel.onEncodedFrame(frame.isKeyFrame, frame.isCodecConfig, frame.presentationTimeUs) },
            onError = { viewModel.onCameraError(it.message ?: "Encoder failed") },
        )
        val session = EncoderCameraSession(
            context.applicationContext, encoder, viewModel::onEncoderStarted,
            { viewModel.onCameraError(it.message ?: "Encoder camera failed") },
        )
        runCatching { session.start() }.onFailure { viewModel.onCameraError(it.message ?: "Encoder could not start") }
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) { session.close(); viewModel.endEncoding() }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer); session.close() }
    }
}

@Composable
private fun PermissionRequired(onRequestPermission: () -> Unit, modifier: Modifier = Modifier) {
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text("Camera permission is required.")
            Button(onClick = onRequestPermission, modifier = Modifier.padding(top = 12.dp)) { Text("Grant Permission") }
        }
    }
}

@Preview(showBackground = true)
@Composable
private fun PermissionRequiredPreview() {
    MaterialTheme { PermissionRequired(onRequestPermission = {}) }
}
