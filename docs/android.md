# Android development

Requirements: JDK 17, Android SDK Platform 35, and Build Tools 35.0.0. Set `ANDROID_HOME` or create an untracked `android/local.properties` containing `sdk.dir=C:\\path\\to\\Android\\Sdk`.

Build from PowerShell:

```powershell
cd android
.\gradlew.bat assembleDebug
```

Install and launch the debug APK on an attached device:

```powershell
adb install -r app\build\outputs\apk\debug\app-debug.apk
adb shell am start -n id.webcamku.app/.MainActivity
```

## Camera preview (M0.1)

The app requests `CAMERA` at runtime, binds a CameraX `Preview` to the activity lifecycle, starts on the back camera, and can switch between back and front cameras. It intentionally requests no microphone permission and contains no encoding or network transport.

## H.264 encoder (M0.3)

`H264Encoder` configures an AVC `MediaCodec` for 1280×720, 30 FPS, 4 Mbps, and a one-second I-frame interval. Its input `Surface` is the sole target of a Camera2 recording session during the local encoder test. Output callbacks copy each encoded access unit before releasing the codec buffer and preserve presentation timestamps plus codec-config/keyframe flags.

Use **Test H.264 Encoder** to replace the CameraX preview temporarily with the local encoder diagnostic. **Stop Encoder Test** releases the capture session, camera, codec, surface, and handler threads, then restores preview. M0.3 does not transmit or persist encoded payloads.

## Wi-Fi transport (M0.4)

**Start Server** opens TCP port 4747 and waits for one Windows client. After HELLO/HELLO_ACK and STREAM_START, the server starts the 720p H.264 encoder and emits binary VIDEO_CONFIG, VIDEO_FRAME, and periodic STATS packets. B-frames are disabled and the encoder-to-network queue retains only the newest pending output; a slow socket drops stale video instead of turning backlog into visible latency. **Stop Wi-Fi Server** closes the listener, active socket, encoder, and camera session.

During streaming, Windows can send `switchCamera`, `torch`, and `zoom` commands. Camera
switch preserves the encoder and rebinds its input surface to the other camera. Torch
and digital zoom modify the active repeating Camera2 request. Unsupported controls and
invalid values produce unsuccessful `COMMAND_ACK` responses without disconnecting.

M0.7 lifecycle handling closes the server, socket, camera session, MediaCodec, and
recovery executor when the activity stops. A runtime encoder or Camera2 failure triggers
a single delayed pipeline restart while the stream remains requested; repeated callbacks
cannot create concurrent restart loops.

## Streaming camera controls (M0.10)

Starting the server now opens one Camera2 session with both the MediaCodec surface and
the on-screen SurfaceView as capture targets. The preview therefore remains live before
and during a Windows connection without competing with the encoder for the camera.
**Dim screen** lowers only the activity brightness and keeps the screen/capture session
awake; **Restore screen** returns to system brightness. Continuous video autofocus is
the default. Moving the Far/Near slider disables autofocus and applies the camera's
supported lens-focus-distance range; unsupported fixed-focus cameras report a clean
status error.

The main screen is responsive rather than scroll-based. Landscape uses camera preview
and controls side by side; portrait keeps a 16:9 preview above a compact control panel.
The primary camera, encoder/server, dim, autofocus, manual-focus, status, and stop
controls remain visible on screen in either orientation.
