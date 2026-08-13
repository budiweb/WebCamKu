package id.webcamku.app

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.DisposableEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import id.webcamku.app.encoding.EncodedFrame
import id.webcamku.app.encoding.EncoderCameraSession
import id.webcamku.app.encoding.H264Encoder
import id.webcamku.app.network.StreamingSession

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
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) ==
                PackageManager.PERMISSION_GRANTED,
        )
    }
    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted -> hasCameraPermission = granted }

    LaunchedEffect(Unit) {
        if (!hasCameraPermission) permissionLauncher.launch(Manifest.permission.CAMERA)
    }

    val state by cameraViewModel.state.collectAsStateWithLifecycle()

    MaterialTheme {
        Scaffold(topBar = { TopAppBar(title = { Text("WebCamKu") }) }) { padding ->
            Column(
                modifier = Modifier.fillMaxSize().padding(padding).padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                if (hasCameraPermission && !state.isEncoding && !state.isServerRunning) {
                    CameraPreview(
                        lensFacing = state.facing.lensFacing,
                        onCameraReady = cameraViewModel::onCameraReady,
                        onCameraError = cameraViewModel::onCameraError,
                        modifier = Modifier.fillMaxWidth().weight(1f),
                    )
                } else {
                    PermissionRequired(
                        onRequestPermission = {
                            permissionLauncher.launch(Manifest.permission.CAMERA)
                        },
                        modifier = Modifier.fillMaxWidth().weight(1f),
                    )
                }

                Text("Status: ${state.status}")
                Text("Camera: ${state.facing.label}")
                if (state.isServerRunning) {
                    ServerSession(cameraViewModel)
                    Text("Port: 4747")
                    Text("Sent: ${state.streamedFrames} frames | ${state.streamedBytes} bytes")
                    Text("Dropped: ${state.droppedFrames}")
                    Button(onClick = cameraViewModel::stopServer, modifier = Modifier.fillMaxWidth()) {
                        Text("Stop Wi-Fi Server")
                    }
                } else if (state.isEncoding) {
                    EncoderSession(cameraViewModel)
                    Text("Frames: ${state.encodedFrames} | Key: ${state.keyFrames} | Config: ${state.configFrames}")
                    Text("Timestamp: ${state.lastTimestampUs} µs")
                    Button(onClick = cameraViewModel::endEncoding, modifier = Modifier.fillMaxWidth()) {
                        Text("Stop Encoder Test")
                    }
                } else {
                    Button(
                        onClick = cameraViewModel::switchCamera,
                        enabled = hasCameraPermission && state.isReady,
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("Switch Camera") }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(
                            onClick = cameraViewModel::beginEncoding,
                            enabled = hasCameraPermission && state.isReady,
                            modifier = Modifier.width(160.dp),
                        ) { Text("Test Encoder") }
                        Button(
                            onClick = cameraViewModel::startServer,
                            enabled = hasCameraPermission && state.isReady,
                            modifier = Modifier.width(160.dp),
                        ) { Text("Start Server") }
                    }
                }
            }
        }
    }
}

@Composable
private fun ServerSession(viewModel: CameraViewModel) {
    val context = LocalContext.current
    DisposableEffect(context) {
        val session = StreamingSession(
            context,
            viewModel::onServerStatus,
            { stats -> viewModel.onServerStats(stats.framesSent, stats.bytesSent, stats.droppedFrames) },
        )
        runCatching { session.start() }.onFailure {
            viewModel.onServerStatus("Server could not start: ${it.message}")
        }
        onDispose { session.close() }
    }
}

@Composable
private fun EncoderSession(viewModel: CameraViewModel) {
    val context = LocalContext.current
    DisposableEffect(context) {
        val encoder = H264Encoder(
            onFrame = { frame: EncodedFrame ->
                viewModel.onEncodedFrame(frame.isKeyFrame, frame.isCodecConfig, frame.presentationTimeUs)
            },
            onError = { viewModel.onCameraError(it.message ?: "Encoder failed") },
        )
        val session = EncoderCameraSession(
            context.applicationContext,
            encoder,
            viewModel::onEncoderStarted,
            { viewModel.onCameraError(it.message ?: "Encoder camera failed") },
        )
        // CameraX is unbound by the preview's preceding disposal before this effect runs.
        runCatching { session.start() }.onFailure {
            viewModel.onCameraError(it.message ?: "Encoder could not start")
        }
        onDispose { session.close() }
    }
}

@Composable
private fun PermissionRequired(onRequestPermission: () -> Unit, modifier: Modifier = Modifier) {
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text("Camera permission is required to show the preview.")
            Button(onClick = onRequestPermission, modifier = Modifier.padding(top = 12.dp)) {
                Text("Grant Camera Permission")
            }
        }
    }
}

@Preview(showBackground = true)
@Composable
private fun PermissionRequiredPreview() {
    MaterialTheme { PermissionRequired(onRequestPermission = {}) }
}
