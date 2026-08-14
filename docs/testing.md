# Testing

## M0 automated verification

```powershell
cd android
.\gradlew.bat assembleDebug

cd ..\windows
dotnet restore WebCamKu.sln
dotnet build WebCamKu.sln --no-restore
```

M0 has no business logic requiring unit tests. Build success is the automated gate.

## M0 manual verification

1. Install the Android debug APK and launch WebCamKu. Confirm the static Compose screen displays without a crash.
2. Run `dotnet run --project windows\WebCamKu.Client\WebCamKu.Client.csproj`. Confirm the WPF window opens and closes cleanly.
3. Confirm neither application exposes camera or streaming behavior; those belong to later milestones.

Record device/OS details and results before checking off launch acceptance.

## Latest M0 verification

Verified on 2026-08-14:

- `android\gradlew.bat assembleDebug`: passed; debug APK produced.
- `dotnet restore WebCamKu.sln`: passed.
- `dotnet build WebCamKu.sln --no-restore`: passed with 0 warnings and 0 errors.
- Windows client launch smoke test: passed; the process remained running and exposed a main window titled `WebCamKu`.
- Android launch smoke test: pending because no Android device or emulator was attached to this workstation. The APK build is verified, but launch acceptance must not be claimed until it is run on Android API 26 or newer.

## Latest M0.1 verification

Verified on 2026-08-14 using a connected Samsung SM-M315F where possible:

- Runtime permission flow: passed; a clean install starts with `CAMERA` denied and requests it at runtime.
- Android debug build: passed.
- Camera state unit tests: passed.
- Live preview: passed on the back camera with status `Preview active`.
- Front/back switching: passed; the front camera reached `Preview active`.
- Rotation: passed; preview remained active through landscape and portrait transitions without a fatal exception.

## Latest M0.2 verification

- Android packet writer and message tests: passed.
- Windows WKC/1 parser/writer tests: passed for header round-trip, split reads, concatenated packets, invalid magic/version/length, JSON validation, and unsigned sequence/timestamp boundaries.
- No TCP connection or video path is present; those remain later milestones.

## Latest M0.3 verification

Verified on 2026-08-14 using Samsung SM-M315F:

- 1280×720 AVC encoder reached the active recording state.
- First 12-second sample produced 339 encoded outputs, 12 keyframes, one codec-config output, and non-zero increasing presentation timestamps.
- Stop returned to an active CameraX preview.
- Restart produced 280 outputs, 10 keyframes, one codec-config output, and non-zero timestamps in the observed interval.
- No fatal exception or codec exception was recorded.
- `testDebugUnitTest` and `assembleDebug` passed.

## Latest M0.4 verification

Verified on 2026-08-14 over Wi-Fi between Samsung SM-M315F (`192.168.1.24`) and the Windows workstation:

- HELLO, HELLO_ACK, and STREAM_START handshake passed.
- Windows received codec configuration and continuous non-empty H.264 video frames.
- A continuous 30-minute receive run completed without an application or transport crash.
- The Android sender queue remained bounded at four encoded outputs and reported zero drops in sampled UI diagnostics.
- Windows test-host working set was approximately 72 MB initially and approximately 75 MB at 30 minutes, with no continuous growth trend.
- Android PSS was approximately 105 MB initially and approximately 81 MB at 13 minutes, with no continuous growth trend.
- No decoding or Windows video preview is present; that remains M0.5.

## Latest M0.5 verification

Verified on 2026-08-14 using Samsung SM-M315F and the Windows Release build:

- Media Foundation decoded live H.264 to 1280×720 frames with zero decoder errors in the physical integration test.
- WPF rendered the live phone camera and remained responsive.
- After warm-up, rolling preview rate held approximately 29.8–29.9 FPS for the uninterrupted sampled portion of the long run.
- The preview process remained alive beyond 30 minutes. A workstation scheduling/sleep gap occurred near the end, so that interval is excluded from FPS calculation but not from crash/resource-lifetime validation.
- Working set stabilized around 203–208 MB after warm-up; the per-frame allocation defect found during an earlier rejected run was removed.
- Reconnect passed without restarting either application: the Android listener was stopped and started, the same Windows process reconnected, and preview resumed at 29.8 FPS.

## Latest M0.6 verification

- Android `testDebugUnitTest assembleDebug` passed on 2026-08-14.
- Windows Release native/managed build passed with zero warnings and zero errors.
- Windows automated suite passed: 11 Core tests plus 1 Client decoder test.
- Physical control test passed on Samsung SM-M315F over Wi-Fi: 2.0x zoom ACK succeeded
  and frames continued; back-camera torch on/off ACKs succeeded; switch to front ACK
  succeeded and frames resumed; front-camera torch returned a clean unsuccessful ACK;
  switching back succeeded. Both applications remained running throughout the test.

## Latest M0.7 verification

- Android unit tests and debug APK build passed on 2026-08-14.
- Windows Release native/managed build passed with zero warnings and zero errors.
- Automated tests cover bounded reconnect delays, handshake timeout, mid-stream
  disconnect, rapid connect/disconnect, and Media Foundation decoder reset/recovery.
- Physical Samsung SM-M315F scenarios passed without crashes: Wi-Fi off/on recovered in
  the same Windows process; Android force-close/restart recovered; server stop/start
  recovered; rotation kept Android alive and streaming resumed; a 15-second Windows
  process suspend/resume continued at about 30 FPS; eight rapid connections followed by
  a normal stream succeeded.
- A continuous 30.17-minute run produced 54,067 rendered frames and ended at 30.0 FPS.
  Per-minute rolling samples remained about 28.8-31.0 FPS. Windows working set sampled
  from 203-220 MB and ended at 212 MB, with no continuous growth.
- Native Release build, managed Release build, Android build/tests, and all Windows tests passed.

## Latest M0.8 verification

- `WebCamKu Camera (Windows Virtual Camera)` registered, unregistered, and registered again cleanly on Windows 11.
- A Media Foundation camera consumer opened 30 consecutive 1280x720 NV12 samples; Chrome and OBS selected the virtual camera.
- Live phone frames appeared in OBS. Disconnect retained the last coherent frame without crashing the consumer, reconnect resumed publication, and the Windows client restarted cleanly.
- Start/stop/start from the non-elevated client succeeded through the installed persistent host.
- Shared-frame collisions retry and retain the last valid frame instead of alternating with the placeholder; this removed the reported OBS flicker.
- The Android/network/decode path uses one-frame latest-only queues, no H.264 B-frames, and Media Foundation low-latency mode for audio/video alignment.
- Windows Release build passed with zero warnings and errors; 15 Core plus 2 Client tests passed; Android debug build and unit tests passed.

## Latest M0.9 verification

- The client found repository Android Platform-Tools and detected authorized Samsung SM-M315F serial `RR8R407VQ1V`.
- With phone Wi-Fi disabled, `adb forward tcp:4747 tcp:4747` carried the existing WKC/1 stream through USB with no root or custom driver.
- Both physical Media Foundation decode/recovery tests passed through `127.0.0.1`.
- The actual WPF **USB (ADB)** mode created its own forward and rendered 242 frames at 28.9 FPS during the sampled run.
- Closing the WPF window removed the scoped forwarding rule; Wi-Fi was restored afterward.
- Parser tests cover authorized, unauthorized, offline, daemon, and empty device-list output. The full Windows suite passed: 15 Core and 4 Client tests.

## Latest M0.10 verification

- The native x64 OBS plugin built against the matching OBS 32.2.1 headers and loaded in
  normal OBS mode as `webcamku-obs.dll`.
- Android `assembleDebug` and `testDebugUnitTest` passed, and the APK was installed on
  Samsung SM-M315F.
- Starting the server displayed the streaming preview and kept the physical stream
  active; the sampled counter reached 2,190 frames without a fatal camera exception.
- Dim/restore state changed correctly while streaming. Continuous autofocus was the
  default, moving the focus slider changed the UI to manual focus, and frames continued.
- Final visual confirmation of a newly added **WebCamKu Source** in OBS remains the last
  manual gate before M0.10 is complete.
- Responsive Android acceptance passed in physical landscape mode: preview, Switch
  Camera, Test Encoder, Start/Stop Server, Dim, Auto Focus, and manual focus were all
  reachable without scrolling.
- A preview regression was reproduced with the client receiving 3.9 Mbps but rendering
  zero frames. Retaining/resending VIDEO_CONFIG and requesting a connection keyframe
  fixed startup after the Android encoder was already active.
- The Windows Release client then rendered 408 physical USB frames at 30.1 FPS with
  zero dropped frames and zero decoder errors.
