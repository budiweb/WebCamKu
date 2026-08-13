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
