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

**Start Server** opens TCP port 4747 and waits for one Windows client. After HELLO/HELLO_ACK and STREAM_START, the server starts the 720p H.264 encoder and emits binary VIDEO_CONFIG, VIDEO_FRAME, and periodic STATS packets. The encoder-to-network queue is bounded to four outputs; when full, its oldest output is discarded so backlog cannot grow without limit. **Stop Wi-Fi Server** closes the listener, active socket, encoder, and camera session.
