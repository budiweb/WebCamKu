# Windows development

Requirements: Windows, the .NET 10 SDK, Visual Studio Build Tools 2022 with the C++ workload, and a Windows 10/11 SDK.

```powershell
cd windows
dotnet restore WebCamKu.sln
dotnet build WebCamKu.sln --no-restore
dotnet run --project WebCamKu.Client\WebCamKu.Client.csproj
```

From M0.5 onward, use the repository build script so the native Media Foundation decoder is built before the managed projects:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\build.ps1 -Configuration Release
```

The script locates MSBuild through `vswhere`, compiles the x64 native decoder, restores/builds the .NET solution, and copies `WebCamKu.Video.dll` beside the WPF executable.

## Encoded transport test (M0.4)

Start the Android Wi-Fi server, enter the phone IP in the Windows client, and connect. The client validates the WKC/1 handshake and reports received H.264 frames/configuration without decoding them.

## Windows preview (M0.5)

The native `WebCamKu.Video` layer owns Media Foundation/COM decoder details. It accepts H.264 elementary-stream access units, enables the decoder's low-latency mode, decodes to NV12, and currently converts decoded frames to BGRA for WPF. The managed decode pipeline retains only the newest pending encoded packet and reports exact dropped-frame and decoder-error counts. WPF keeps at most one render pending and reuses frame buffers to prevent dispatcher backlog and continuous memory growth.

M0.6 adds Switch Camera, Torch, and Zoom controls below the preview. Controls are enabled
only after the connection reaches `Streaming`. Each request waits for its correlated
Android `COMMAND_ACK`; a rejected or unsupported operation is shown in the status text
and leaves the stream connected.

M0.7 automatically reconnects after network loss, phone shutdown, or server restart.
Retry delays are 1, 2, 3, then at most 5 seconds. Disconnect cancels reconnect. Connect,
handshake, and inactive-stream operations have explicit 10-second timeouts, and status
text distinguishes unreachable devices, timeouts, and clean disconnects. Decoder errors
flush the Media Foundation transform and reapply the latest H.264 configuration.

Run the physical-device continuity test with PowerShell execution-policy bypass if local policy blocks scripts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\test-m0.4.ps1 -PhoneIp 192.168.1.24
```

## Windows 11 virtual camera (M0.8)

Build Release, then run the development installer once from an elevated PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\install-virtual-camera.ps1
```

The WPF Start/Stop Virtual Camera buttons signal the installed persistent host. Consumers
select `WebCamKu Camera (Windows Virtual Camera)`. In OBS use Device Default and disable
source buffering for the lowest latency. The shared publisher runs before WPF repaint
scheduling, retries writer collisions, and holds the last coherent frame on disconnect.

## USB via ADB (M0.9)

Enable USB debugging, connect and authorize one Android phone, start the Android server,
then select **USB (ADB)** in the Windows Connection Mode. The client discovers ADB,
forwards TCP 4747, connects through localhost, retries cleanly, and removes the forward
on Disconnect. See [usb.md](usb.md) for setup and error guidance.

## Native OBS source (M0.10)

The plugin targets the installed OBS 32.2.1 API and reads the same latest-frame shared
surface as the virtual camera, but publishes BGRA frames directly through an asynchronous
OBS input source. Build and install it with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\build-obs-plugin.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\install-obs-plugin.ps1
```

The standard installation uses OBS's Windows plugin path under
`C:\ProgramData\obs-studio\plugins` and does not modify the OBS installation. For a
machine installation under Program Files, run an elevated shell and add
`-Scope Machine`. Restart OBS, choose **Add Source**, then select **WebCamKu Source**.
The Windows client must remain connected because it owns the decoded shared-frame
publisher.
