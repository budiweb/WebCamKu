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

The M0 screen is a static Compose bootstrap. It intentionally requests no camera or microphone permission.

