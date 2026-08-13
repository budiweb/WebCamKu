# Windows development

Requirements: Windows and the .NET 10 SDK.

```powershell
cd windows
dotnet restore WebCamKu.sln
dotnet build WebCamKu.sln --no-restore
dotnet run --project WebCamKu.Client\WebCamKu.Client.csproj
```

M0 uses managed WPF only. Visual Studio C++ tooling and native MSBuild are not required until a later video or virtual-camera milestone.

## Encoded transport test (M0.4)

Start the Android Wi-Fi server, enter the phone IP in the Windows client, and connect. The client validates the WKC/1 handshake and reports received H.264 frames/configuration without decoding them.

Run the physical-device continuity test with PowerShell execution-policy bypass if local policy blocks scripts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\test-m0.4.ps1 -PhoneIp 192.168.1.24
```
