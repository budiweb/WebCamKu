# USB mode (M0.9)

USB mode uses Android Debug Bridge port forwarding; it does not install a custom USB
driver and does not require root. The Android app continues to listen on device port
4747 while the Windows client connects to `127.0.0.1:4747`.

## Phone setup

1. Enable Developer options and USB debugging on Android.
2. Connect the phone with a data-capable USB cable.
3. Unlock the phone and accept the RSA **Allow USB debugging** prompt.
4. Open WebCamKu and press **Start Server**. The button retains its Wi-Fi-era label,
   but the same local TCP server serves both Wi-Fi and forwarded USB connections.

## Windows client

Select **USB (ADB)** in Connection Mode and press **Connect**. The phone-IP field is
disabled because USB always connects through localhost. WebCamKu searches for `adb.exe`
in this order:

1. `WEBCAMKU_ADB_PATH`;
2. `ANDROID_HOME\platform-tools`;
3. the standard per-user Android SDK;
4. the repository `.tools\android-sdk` development SDK;
5. `PATH`.

The client requires exactly one authorized online device, runs:

```text
adb -s <serial> forward tcp:4747 tcp:4747
```

and removes that forwarding rule on Disconnect or clean application close. Missing ADB,
no device, unauthorized/offline devices, multiple devices, and forwarding failures are
reported separately. USB reconnect retries use the existing bounded reconnect policy.

## Manual diagnostics

```powershell
adb devices -l
adb forward --list
```

`device` means authorized. If `unauthorized` is shown, unlock the phone and accept its
USB-debugging prompt.
