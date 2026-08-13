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
