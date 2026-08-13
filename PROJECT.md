# PROJECT.md — WebCamKu

> Working title: **WebCamKu**  
> Product type: Android phone-as-webcam application for Windows  
> Target: Android + Windows first  
> Development style: incremental, testable, production-minded  
> Primary goal: Android camera → low-latency stream → Windows client → Windows virtual webcam

---

## 1. Instructions for Codex

You are working on **WebCamKu**, an original Android-to-Windows webcam application inspired by the general product category of apps such as DroidCam.

### Important rules

1. Build this project **incrementally by milestone**.
2. Do **not** attempt to implement the entire product in one pass.
3. Keep the repository buildable after every meaningful change.
4. Prefer simple, testable architecture over clever abstractions.
5. Do not copy proprietary DroidCam code, assets, trademarks, UI, protocol, or reverse-engineered behavior.
6. Use only documented/public APIs and original implementation.
7. Do not add cloud services, accounts, analytics, ads, subscriptions, or external backend services unless explicitly requested.
8. The first usable product must work entirely on a local network.
9. Do not introduce WebRTC until the basic H.264/TCP implementation is stable.
10. Do not implement audio until video streaming and virtual camera are stable.
11. Do not implement a Windows kernel-mode camera driver.
12. Windows 11 virtual camera should use the supported Media Foundation virtual camera API where practical.
13. Windows 10 compatibility must not block the initial Windows 11 virtual-camera milestone.
14. Avoid premature optimization, but instrument latency, FPS, bitrate, dropped frames, and connection state from the beginning.
15. For every milestone:
    - implement;
    - build;
    - run tests;
    - document manual verification;
    - update the checklist in this file.

If an architectural assumption in this document proves invalid on a real device, document the reason and make the smallest reasonable change.

---

# 2. Product Vision

Turn an Android phone into a practical webcam for a Windows PC.

The final user experience should be:

1. Install WebCamKu on Android.
2. Install WebCamKu Client on Windows.
3. Put both devices on the same Wi-Fi network or connect the phone by USB.
4. Open the Android app.
5. Open the Windows client.
6. Windows discovers the phone or the user enters its IP address.
7. Click **Connect**.
8. The phone camera appears in the Windows preview.
9. Start the virtual camera.
10. Applications such as OBS, browser-based meeting apps, and compatible Windows camera applications can select:

   **WebCamKu Camera**

The system should prioritize:

- low latency;
- stable streaming;
- simple setup;
- good image quality;
- low CPU usage;
- hardware video encoding/decoding where available;
- predictable reconnect behavior.

---

# 3. Initial Supported Platforms

## Android

Initial target:

- Android 8.0 / API 26 or newer.
- ARM64 devices are the priority.
- CameraX is the primary camera abstraction.
- MediaCodec is the primary H.264 encoder.

Do not initially support:

- Android below API 26;
- rooted-device-only features;
- vendor-specific camera APIs;
- background streaming with the screen permanently off;
- advanced Camera2-only manual controls.

## Windows

### Windows viewer

Target:

- Windows 10 64-bit;
- Windows 11 64-bit.

### Virtual webcam

First implementation target:

- Windows 11 build 22000 or newer.

Use Windows Media Foundation virtual camera APIs where suitable.

Windows 10 virtual-camera compatibility is a later compatibility milestone and must not delay the Windows 11 implementation.

---

# 4. Technology Stack

## Android

Language:

- Kotlin

UI:

- Jetpack Compose

Camera:

- AndroidX CameraX

Encoding:

- Android MediaCodec

Networking:

- Kotlin coroutines
- Java/Kotlin sockets for the first protocol implementation

State:

- ViewModel + StateFlow

Persistence:

- DataStore for settings only

Build:

- Gradle Kotlin DSL

## Windows UI

Language:

- C#

Runtime:

- .NET 10

UI:

- WPF

Architecture:

- MVVM, but keep it lightweight.

Networking:

- System.Net.Sockets
- async/await
- CancellationToken

Logging:

- Microsoft.Extensions.Logging

## Windows video pipeline

Preferred native layer:

- C++

Primary APIs:

- Media Foundation
- Windows Media Foundation virtual camera API

Interop:

- expose only the smallest necessary native surface to C#;
- avoid spreading P/Invoke or COM details throughout the C# application.

---

# 5. Repository Layout

Use a monorepo.

```text
webcamku/
│
├── PROJECT.md
├── README.md
├── LICENSE
├── .gitignore
│
├── android/
│   ├── app/
│   ├── build.gradle.kts
│   ├── settings.gradle.kts
│   └── gradle/
│
├── windows/
│   ├── WebCamKu.sln
│   │
│   ├── WebCamKu.Client/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Services/
│   │   ├── Models/
│   │   └── App.xaml
│   │
│   ├── WebCamKu.Core/
│   │   ├── Networking/
│   │   ├── Protocol/
│   │   ├── Streaming/
│   │   └── Diagnostics/
│   │
│   ├── WebCamKu.Video/
│   │   ├── Decoder/
│   │   ├── Rendering/
│   │   └── Interop/
│   │
│   └── WebCamKu.VirtualCamera/
│       ├── MediaSource/
│       ├── Registration/
│       └── Native/
│
├── protocol/
│   ├── protocol.md
│   ├── messages.md
│   └── examples/
│
├── scripts/
│   ├── windows/
│   └── android/
│
├── installer/
│
├── docs/
│   ├── architecture.md
│   ├── android.md
│   ├── windows.md
│   ├── usb.md
│   ├── troubleshooting.md
│   └── testing.md
│
└── tests/
```

Do not create unnecessary microprojects.

---

# 6. High-Level Architecture

```text
ANDROID
─────────────────────────────────

CameraX
   │
   ├── Preview
   │
   └── Encoder input Surface
            │
            ▼
       MediaCodec
       H.264 / AVC
            │
            ▼
       Stream Server
            │
       TCP connection
            │
════════════╪══════════════════════
            │
            ▼
WINDOWS
─────────────────────────────────

       Stream Client
            │
            ▼
      Packet Parser
            │
            ▼
       H.264 Decoder
            │
            ├──────────► Preview Renderer
            │
            ▼
       Frame Pipeline
            │
            ▼
      Virtual Camera
            │
            ▼
OBS / Browser / Meeting Application
```

Control messages travel in the opposite direction:

```text
Windows
   │
   │ JSON control message
   ▼
Android

switch camera
torch
focus
zoom
resolution
FPS
bitrate
stop/start stream
```

---

# 7. Core Architectural Principles

## 7.1 Separate control and media concepts

The protocol may initially share a TCP connection, but logically distinguish:

- session/control messages;
- encoded video packets;
- keepalive;
- errors;
- statistics.

Do not mix protocol parsing into UI code.

## 7.2 Encoded video over the network

Do not stream a sequence of JPEG screenshots.

Primary format:

- H.264 / AVC

Initial profile should prioritize decoder compatibility and low latency.

## 7.3 Hardware acceleration

Android:

- use MediaCodec encoder;
- prefer hardware AVC encoder where available.

Windows:

- prefer Media Foundation hardware decoding when available;
- gracefully fall back if necessary.

## 7.4 Bounded buffering

Latency is more important than preserving every frame.

Never allow an unbounded queue of video frames.

When the receiver falls behind:

- drop stale non-keyframes where safe;
- recover at the next keyframe;
- report dropped frame count.

---

# 8. Network Protocol — V1

The first protocol must be intentionally simple.

Protocol name:

**WKC/1 — WebCamKu Camera Protocol v1**

Default TCP port:

```text
4747
```

This port must be configurable.

---

## 8.1 TCP stream framing

Every message begins with a fixed binary header.

Recommended initial header:

```text
Offset  Size  Field
0       4     Magic = "WKC1"
4       1     Version = 1
5       1     MessageType
6       2     Flags
8       4     PayloadLength, unsigned big-endian
12      8     TimestampUs, unsigned big-endian
20      4     SequenceNumber, unsigned big-endian
```

Header size:

```text
24 bytes
```

The payload immediately follows the header.

Set a strict maximum payload size.

Reject malformed or excessively large packets.

---

## 8.2 Message types

Initial message types:

```text
0x01 HELLO
0x02 HELLO_ACK
0x03 VIDEO_CONFIG
0x04 VIDEO_FRAME
0x05 COMMAND
0x06 COMMAND_ACK
0x07 PING
0x08 PONG
0x09 STATS
0x0A ERROR
0x0B STREAM_START
0x0C STREAM_STOP
```

---

## 8.3 HELLO

Android sends HELLO after connection.

Payload format:

UTF-8 JSON.

Example:

```json
{
  "deviceName": "Android Phone",
  "appVersion": "0.1.0",
  "protocolVersion": 1,
  "cameras": [
    {
      "id": "0",
      "facing": "back"
    },
    {
      "id": "1",
      "facing": "front"
    }
  ],
  "video": {
    "codec": "video/avc",
    "width": 1280,
    "height": 720,
    "fps": 30,
    "bitrate": 4000000
  }
}
```

Do not expose unnecessary device identifiers such as IMEI, serial number, account details, or MAC address.

---

## 8.4 HELLO_ACK

Windows response:

```json
{
  "accepted": true,
  "protocolVersion": 1
}
```

If incompatible:

```json
{
  "accepted": false,
  "reason": "Unsupported protocol version"
}
```

---

## 8.5 VIDEO_CONFIG

Send codec initialization data before normal frames and whenever encoder configuration changes.

For H.264 this may contain the required SPS/PPS data.

The transport must not assume that every decoder can begin from an arbitrary P-frame.

---

## 8.6 VIDEO_FRAME

Payload:

- one encoded H.264 access unit or clearly documented NAL-unit bundle;
- timestamp from the Android capture/encoder timeline.

Flags should identify at least:

```text
KEY_FRAME
CONFIG
END_OF_STREAM
```

Do not send Base64 video through JSON.

---

# 9. Connection Lifecycle

Expected sequence:

```text
Android stream server starts
        │
        ▼
Windows connects
        │
        ▼
Android → HELLO
        │
        ▼
Windows → HELLO_ACK
        │
        ▼
Windows → STREAM_START
        │
        ▼
Android configures encoder
        │
        ▼
Android → VIDEO_CONFIG
        │
        ▼
Android → VIDEO_FRAME ...
        │
        ▼
Windows decodes + renders
```

Shutdown:

```text
STREAM_STOP
or
socket disconnect
```

All resources must be released predictably.

---

# 10. Reconnect Behavior

Windows must handle:

- Wi-Fi temporarily lost;
- Android app closed;
- phone IP changed;
- socket timeout;
- decoder failure.

Initial reconnect policy:

```text
1 second
2 seconds
3 seconds
5 seconds
5 seconds
...
```

Maximum automatic retry interval:

```text
5 seconds
```

The user must be able to cancel reconnect.

Do not create an infinite busy loop.

---

# 11. Android Application Requirements

Working package name:

```text
id.webcamku.app
```

This can be renamed before public release.

---

## 11.1 Android screens

Initial application should have one main screen.

Example structure:

```text
┌──────────────────────────────────┐
│ WebCamKu                         │
│                                  │
│ ┌──────────────────────────────┐ │
│ │                              │ │
│ │       Camera Preview         │ │
│ │                              │ │
│ └──────────────────────────────┘ │
│                                  │
│ Status: Ready                    │
│ IP: 192.168.1.20                 │
│ Port: 4747                       │
│                                  │
│ Camera: Back                     │
│ Quality: 720p / 30 FPS           │
│                                  │
│ [ Start Server ]                 │
│                                  │
│ [ Switch Camera ] [ Torch ]      │
└──────────────────────────────────┘
```

---

## 11.2 Android permissions

Use only necessary permissions.

Expected:

```text
CAMERA
INTERNET
ACCESS_NETWORK_STATE
ACCESS_WIFI_STATE
```

Microphone permission must not be requested until the audio milestone.

Avoid requesting broad storage permission.

---

## 11.3 CameraX

Responsibilities:

```text
CameraManager
├── bind camera
├── preview
├── front/back selection
├── torch
├── zoom
└── focus where supported
```

Do not put socket logic inside CameraManager.

---

## 11.4 Encoder

Create a dedicated component:

```text
H264Encoder
```

Responsibilities:

- configure MediaCodec;
- use AVC/H.264;
- expose encoder input Surface;
- read encoded output;
- identify codec-config packets;
- identify keyframes;
- preserve presentation timestamps;
- support clean stop/restart.

Initial target presets:

### 720p

```text
1280 × 720
30 FPS
4 Mbps default
```

### 1080p

Later milestone:

```text
1920 × 1080
30 FPS
6 Mbps default
```

Bitrate should later be configurable.

---

## 11.5 Android foreground behavior

For the first MVP:

- streaming is expected while the app is open.

Later:

- move active streaming into a foreground service;
- show a persistent notification;
- make start/stop lifecycle explicit.

Do not make background execution complexity block milestone 0.1.

---

# 12. Windows Client Requirements

Main executable:

```text
WebCamKu.Client.exe
```

---

## 12.1 Initial UI

```text
┌─────────────────────────────────────────┐
│ WebCamKu                                │
├─────────────────────────────────────────┤
│                                         │
│           Camera Preview                │
│                                         │
├─────────────────────────────────────────┤
│ Device IP: [ 192.168.1.20           ]   │
│ Port:      [ 4747                   ]   │
│                                         │
│ [ Connect ]  [ Disconnect ]             │
│                                         │
│ Status: Connected                       │
│ 1280×720 | 30 FPS | 3.8 Mbps | 92 ms    │
│                                         │
│ Camera: [ Back ▼ ]                      │
│ [ Switch ] [ Torch ]                    │
└─────────────────────────────────────────┘
```

UI must remain responsive while streaming.

Never perform socket receive loops or decoding synchronously on the WPF UI thread.

---

# 13. Windows Core Components

Recommended interfaces:

```csharp
IWebCamConnection
IProtocolReader
IProtocolWriter
IVideoReceiver
IVideoDecoder
IVideoFrameSink
IVirtualCameraService
IConnectionDiscovery
```

Do not over-engineer dependency injection.

---

## 13.1 Connection service

Responsibilities:

- connect;
- disconnect;
- cancellation;
- keepalive;
- receive loop;
- send commands;
- reconnect;
- state changes.

States:

```text
Disconnected
Connecting
Handshaking
Streaming
Reconnecting
Error
```

---

## 13.2 Protocol parser

Must safely handle:

- partial TCP reads;
- multiple packets arriving in one read;
- invalid magic;
- unsupported version;
- malformed length;
- disconnect mid-packet.

Never assume one `ReadAsync()` call equals one protocol packet.

---

# 14. Windows Video Decoder

Preferred implementation:

- Media Foundation H.264 decoder.

Input:

```text
encoded H.264 access units
```

Output initial preferred pixel format:

```text
NV12
```

Only convert to BGRA/RGB for UI rendering when necessary.

Avoid unnecessary full-frame copies.

Expose statistics:

- frames received;
- frames decoded;
- frames dropped;
- decoder errors;
- current dimensions;
- decode FPS.

---

# 15. Windows Preview Rendering

MVP priority:

correctness first.

Acceptable initial approach:

- decode to a displayable frame;
- render in WPF.

But design the pipeline so rendering can later be replaced by a lower-copy/native path.

The decoder must not directly depend on WPF View classes.

---

# 16. Windows 11 Virtual Camera

This is milestone 0.3, not milestone 0.1.

Use the documented Windows Media Foundation virtual camera API.

Primary API:

```cpp
MFCreateVirtualCamera(...)
```

Study the official Microsoft Windows-Camera VirtualCamera sample before implementing this module.

Expected logical architecture:

```text
H264 Decoder
    │
    ▼
Decoded Frame Distributor
    │
    ├────────► WPF Preview
    │
    ▼
Virtual Camera Media Source
    │
    ▼
Windows Camera Pipeline
```

Virtual camera display name:

```text
WebCamKu Camera
```

Do not make the virtual camera depend on the WPF preview being visible.

The virtual camera should consume frames from a shared frame provider.

When no phone is connected, provide a defined behavior:

- either a generated placeholder frame;
- or a clean "no frame available" state supported by the media source.

Do not crash applications consuming the camera.

---

# 17. Windows 10 Virtual Camera

Not part of the initial implementation.

Do not implement a legacy DirectShow virtual camera unless explicitly starting the Windows 10 compatibility milestone.

Reason:

- greater implementation and packaging complexity;
- risk of distracting from the core streaming product.

The Windows 10 viewer must still work.

Possible future strategies must be evaluated separately.

---

# 18. USB Mode

USB is milestone 0.4.

Do not invent a custom USB driver.

Use Android Debug Bridge port forwarding for developer/initial USB mode.

Concept:

```text
Android server
127.0.0.1 / device port 4747

       │
       │ USB + ADB
       ▼

Windows
127.0.0.1:4747
```

Expected command:

```bash
adb forward tcp:4747 tcp:4747
```

The Windows client should eventually provide:

```text
Connection Mode
○ Wi-Fi
● USB
```

USB workflow:

1. detect `adb`;
2. detect connected authorized Android device;
3. configure port forwarding;
4. connect Windows client to `127.0.0.1:4747`;
5. clean up forwarding when appropriate.

Do not require root.

Document USB debugging requirements clearly.

---

# 19. Device Discovery

Do not implement discovery in milestone 0.1.

Initial connection:

```text
manual IP + port
```

Later milestone:

- mDNS or another local discovery mechanism.

Discovery must only operate on the local network.

Do not require a cloud directory.

---

# 20. Audio

Audio is intentionally deferred.

Do not request Android microphone permission until the audio milestone.

Future design:

```text
Android AudioRecord
      │
      ▼
Audio Encoder
      │
      ▼
Network
      │
      ▼
Windows Decoder
```

The audio format and synchronization model must be designed separately.

Do not bolt audio packets onto the video path without proper timestamps.

---

# 21. Security

MVP is LAN-only, but basic defensive design is required.

## Required from the start

- validate all packet lengths;
- maximum message payload limits;
- socket timeouts;
- cancellation support;
- do not trust received JSON;
- do not expose sensitive Android identifiers;
- no arbitrary command execution;
- no file transfer protocol;
- bind intentionally to the required network interface;
- log connection attempts without logging sensitive data.

## Later

Add optional session pairing:

```text
6-digit pairing PIN
```

Potential future transport security:

- TLS;
- authenticated session keys.

Do not block the MVP on TLS.

Clearly label early builds as intended for trusted local networks.

---

# 22. Performance Targets

Milestone 0.1 target:

```text
Resolution:        1280×720
Frame rate:        30 FPS
Codec:             H.264
Video bitrate:     ~4 Mbps
Transport:         TCP
Target LAN latency: < 300 ms
```

Stretch goal:

```text
< 150 ms
```

Later:

```text
1920×1080
30 FPS
< 200 ms
```

Do not claim latency without measuring it.

---

# 23. Latency Measurement

Implement timestamp propagation.

Android frame timestamp:

```text
capture/encoder presentation timestamp
```

Protocol:

```text
TimestampUs
```

Windows:

track:

```text
receive time
decode completion time
render time
```

Expose approximate diagnostics.

Note that clocks on two independent devices are not automatically synchronized.

For accurate end-to-end measurement, later add a clock-offset estimation handshake or use an external visual latency test.

---

# 24. Statistics

Windows UI should eventually expose:

```text
Connection uptime
Resolution
FPS
Bitrate
Packets/frames received
Frames decoded
Frames dropped
Decoder errors
Reconnect count
Approximate pipeline latency
```

Android developer diagnostics:

```text
Encoder FPS
Output bitrate
Keyframe count
Socket send rate
Queue depth
Dropped output frames
```

Diagnostics must be optional and must not dominate CPU usage.

---

# 25. Error Handling

Every user-visible failure should have a useful message.

Examples:

```text
Phone not reachable.
Check that the phone and PC are on the same network.

Connection timed out.

Protocol version is not supported.

Camera is being used by another Android application.

H.264 encoder could not be initialized.

Virtual camera requires Windows 11 for this build.

ADB device is not authorized.
Unlock the phone and approve USB debugging.
```

Do not expose raw exception dumps as the primary user message.

Log technical details separately.

---

# 26. Logging

Android:

- Logcat during development;
- structured application logger abstraction if needed.

Windows:

- Microsoft.Extensions.Logging;
- console/debug output in development;
- rolling local file log later.

Log levels:

```text
Trace
Debug
Information
Warning
Error
Critical
```

Never log raw video payloads.

---

# 27. Configuration

Android settings:

```text
port
camera facing
resolution
FPS
bitrate
```

Windows settings:

```text
last IP
port
auto reconnect
connection mode
```

Persist only user settings.

Do not persist transient connection/session state unnecessarily.

---

# 28. Milestones

---

## M0 — Repository bootstrap

Deliver:

- repository structure;
- Android app builds;
- Windows solution builds;
- protocol document;
- basic CI-ready commands documented;
- no streaming yet.

Acceptance:

```text
Android app launches.
Windows client launches.
No build errors.
```

---

## M0.1 — Android Camera Preview

Deliver:

- CameraX preview;
- runtime CAMERA permission;
- front/back switch;
- display current camera state.

Acceptance:

```text
Preview opens reliably.
Switch front/back works.
Rotation does not crash the app.
```

---

## M0.2 — Protocol Skeleton

Deliver:

- shared WKC/1 specification;
- Android packet writer;
- Windows packet reader/writer;
- HELLO / HELLO_ACK;
- PING / PONG;
- parser unit tests.

Acceptance:

- parser handles split TCP reads;
- parser handles multiple packets in one buffer;
- malformed packets are rejected safely.

No real video required yet.

---

## M0.3 — Android H.264 Encoder

Deliver:

- CameraX/Camera pipeline feeding MediaCodec input Surface;
- 720p;
- 30 FPS;
- H.264 AVC;
- codec config extraction;
- encoded-frame callback.

Acceptance:

```text
Encoder starts.
Encoded frames are produced continuously.
Keyframes can be identified.
Encoder can stop and restart.
```

---

## M0.4 — Wi-Fi Video Transport

Deliver:

- Android TCP server;
- Windows connection;
- HELLO handshake;
- VIDEO_CONFIG;
- VIDEO_FRAME;
- statistics.

Acceptance:

```text
Windows receives a valid continuous H.264 stream from the phone.
No unbounded memory growth during a 30-minute test.
```

A preview is not mandatory until the decoder milestone.

---

## M0.5 — Windows H.264 Decode + Preview

Deliver:

- Media Foundation decoder;
- decoded frame path;
- Windows preview;
- FPS counter;
- bitrate;
- dropped frame counter;
- clean connect/disconnect.

This is the first true end-to-end MVP.

Acceptance:

```text
Android camera appears in Windows.
1280×720.
Approximately 30 FPS.
Runs 30 minutes without crash.
Disconnecting and reconnecting does not require restarting either application.
```

Target:

```text
LAN latency below 300 ms on a normal local Wi-Fi network.
```

---

## M0.6 — Remote Camera Controls

Deliver:

- switch front/back from Windows;
- torch if supported;
- digital zoom;
- command ACK/error messages.

Acceptance:

```text
Windows control does not interrupt the stream unnecessarily.
Unsupported features return a clean error.
```

---

## M0.7 — Robustness

Deliver:

- reconnect;
- timeouts;
- encoder restart;
- decoder recovery;
- app lifecycle cleanup;
- bounded queues;
- better error messages.

Test:

- disable/re-enable Wi-Fi;
- close Android app while streaming;
- restart Android server;
- sleep/wake Windows client;
- rotate Android device;
- rapidly connect/disconnect.

Acceptance:

No crashes in expected failure scenarios.

---

## M0.8 — Windows 11 Virtual Camera

Deliver:

- `WebCamKu Camera`;
- Media Foundation virtual camera;
- frame provider shared with Windows preview;
- installer/registration development script;
- start/stop virtual-camera control.

Acceptance:

`WebCamKu Camera` can be selected by at least:

- Windows camera-compatible test application;
- OBS or another common webcam consumer available in the development environment.

The virtual camera continues to expose frames without requiring the preview UI to be continuously repainted.

---

## M0.9 — USB via ADB

Deliver:

- detect ADB;
- detect authorized device;
- `adb forward`;
- USB connection mode;
- clean error messages.

Acceptance:

```text
Video streams through USB with Wi-Fi disabled.
```

Do not require root.

---

## M0.10 — 1080p

Deliver:

```text
1920×1080
30 FPS
configurable bitrate
```

Fallback to 720p when unsupported.

Acceptance:

No regression to 720p stability.

---

## M0.11 — Local Discovery

Deliver:

- discover WebCamKu Android devices on LAN;
- show device list;
- manual IP remains available.

Acceptance:

User can normally connect without typing an IP.

---

## M0.12 — Audio

Only start after video and virtual camera are stable.

Deliver:

- Android microphone capture;
- encoded audio;
- timestamp synchronization;
- Windows receive/decode;
- documented routing design.

Do not compromise video stability.

---

## V1.0 — First Stable Release

Required:

```text
Android → Windows over Wi-Fi
Android → Windows over USB
720p
1080p where supported
30 FPS
front/back camera
zoom
torch where supported
auto reconnect
device discovery
Windows preview
Windows 11 virtual webcam
installer
clear logs
basic diagnostics
documentation
```

Audio can be included only if stable.

---

# 29. Explicit Non-Goals Before V1

Do not implement these unless requested:

- iOS;
- macOS;
- Linux;
- Internet/WAN streaming;
- cloud relay;
- user accounts;
- payment system;
- subscription system;
- advertisements;
- remote recording;
- RTMP output;
- NDI;
- WebRTC;
- H.265;
- AV1;
- 4K;
- 60 FPS;
- AI background removal;
- beauty filters;
- virtual backgrounds;
- multi-phone mixing;
- kernel-mode Windows camera driver.

---

# 30. Testing Strategy

## Unit tests

Protocol:

- header encode/decode;
- invalid magic;
- invalid length;
- split frames;
- concatenated frames;
- JSON validation;
- sequence numbers.

Networking:

- disconnect;
- cancellation;
- timeout;
- reconnect state transition.

## Integration tests

Create a synthetic packet sender so the Windows client can be tested without an Android phone.

Create a test mode capable of sending:

- known protocol packets;
- synthetic timing;
- optionally a small generated H.264 test stream.

Do not make every Windows test require a physical phone.

---

# 31. Manual Test Matrix

Minimum test matrix:

```text
Android device A
Windows 10
Wi-Fi
720p

Android device A
Windows 11
Wi-Fi
720p

Android device A
Windows 11
USB
720p

Android device A
Windows 11
Wi-Fi
1080p

Android front camera
Android back camera
```

Add additional physical devices as they become available.

Record:

- phone model;
- Android version;
- Windows version;
- Wi-Fi type;
- resolution;
- FPS;
- average bitrate;
- approximate latency;
- observed bugs.

---

# 32. Build Requirements

## Android

Expected workflow:

```bash
cd android
./gradlew assembleDebug
```

Windows PowerShell equivalent when appropriate:

```powershell
cd android
.\gradlew.bat assembleDebug
```

## Windows

Expected workflow:

```powershell
cd windows
dotnet restore
dotnet build WebCamKu.sln
```

If the native virtual-camera project requires Visual Studio/MSBuild rather than plain `dotnet build`, document the exact command in:

```text
docs/windows.md
```

---

# 33. Development Environment

Recommended Windows workstation:

- Windows 11 for virtual-camera development;
- Visual Studio with Desktop development with C++;
- current Windows SDK;
- .NET 10 SDK;
- Android Studio;
- Android SDK Platform Tools;
- JDK required by the selected Android Gradle Plugin;
- Git.

Do not add tools without documenting why they are required.

---

# 34. CI

CI is not required for M0, but project layout should allow it.

Later CI jobs:

```text
Android debug build
Windows managed build
protocol unit tests
Windows unit tests
```

Native virtual-camera build may require a Windows runner.

Do not make signing secrets part of the repository.

---

# 35. Installer

Do not build the final installer before the virtual camera works.

Later Windows installer responsibilities:

- install client files;
- install/register virtual-camera component;
- verify prerequisites;
- offer clean uninstall;
- remove registered components correctly.

Android distribution can initially be:

```text
debug APK
```

Later:

```text
signed release APK / app bundle
```

---

# 36. UI/UX Rules

Keep UI simple.

Android primary actions:

```text
Start
Stop
Switch camera
Torch
Settings
```

Windows primary actions:

```text
Select device
Connect
Disconnect
Start Virtual Camera
Stop Virtual Camera
Settings
```

Use clear connection status indicators.

Avoid decorative UI work before the streaming pipeline is reliable.

---

# 37. Threading Rules

## Android

- camera callbacks must not block;
- MediaCodec output handling must not block on slow networking;
- use a bounded queue between encoder and network;
- if network falls behind, prioritize low latency.

## Windows

Separate:

- socket receive;
- protocol parsing;
- decoding;
- frame distribution;
- UI rendering.

Use cancellation tokens.

Do not use `Thread.Sleep()` as synchronization.

---

# 38. Memory Rules

Video applications can accidentally allocate heavily.

Requirements:

- reuse buffers where practical;
- no unbounded `List<byte[]>` of frames;
- use pooled buffers on Windows where appropriate;
- release Media Foundation/COM resources;
- release MediaCodec resources;
- stop CameraX cleanly.

Monitor memory during 30-minute streaming tests.

---

# 39. Frame Queue Policy

Default:

```text
small bounded queue
```

Example target:

```text
2–5 frames maximum
```

If the queue is full:

- do not allow latency to grow indefinitely;
- prefer dropping stale frames;
- request/recover at keyframe if required.

Exact strategy can evolve based on decoder behavior.

---

# 40. H.264 Keyframe Policy

Initial encoder should request reasonably frequent keyframes.

Suggested starting point:

```text
I-frame interval: 1–2 seconds
```

This improves recovery after:

- connection startup;
- dropped frames;
- decoder reset.

Tune later based on bitrate and latency.

---

# 41. Protocol Versioning

Never silently change WKC/1 semantics.

If incompatible protocol changes are needed:

```text
WKC/2
```

HELLO negotiation must reject unsupported versions cleanly.

---

# 42. Documentation Required

Maintain:

```text
README.md
PROJECT.md
protocol/protocol.md
docs/architecture.md
docs/android.md
docs/windows.md
docs/testing.md
docs/troubleshooting.md
```

README should focus on users/developers getting started.

PROJECT.md remains the engineering source of truth.

---

# 43. Source Control Rules

Recommended commit style:

```text
feat(android): add CameraX preview
feat(protocol): implement WKC packet framing
feat(windows): add TCP receiver
feat(video): add H264 decoder
fix(protocol): handle partial packet reads
docs: document USB setup
```

Do not make huge mixed-purpose commits where avoidable.

Never commit:

- signing keys;
- passwords;
- private certificates;
- local machine paths;
- build outputs;
- raw captured user video.

---

# 44. Definition of Done

A milestone is done only when:

1. code builds;
2. relevant tests pass;
3. manual acceptance criteria pass;
4. no known crash in the normal flow;
5. logs are useful;
6. cleanup/disconnect works;
7. documentation reflects the implementation;
8. PROJECT.md checklist is updated.

"Code exists" is not sufficient.

---

# 45. First Codex Execution Plan

Codex should execute the project in this order.

## Step 1

Create repository skeleton only.

Output:

```text
android/
windows/
protocol/
docs/
scripts/
tests/
```

Do not implement video yet.

## Step 2

Create Android app.

Verify:

```text
app launches
Compose UI works
Camera permission flow works
```

## Step 3

Implement CameraX preview.

Verify on physical Android device.

## Step 4

Create Windows WPF client.

Verify:

```text
client launches
connect form works
status model works
```

## Step 5

Implement WKC/1 framing and tests.

No video.

Implement:

```text
HELLO
HELLO_ACK
PING
PONG
ERROR
```

## Step 6

Connect Android and Windows through TCP.

Verify handshake.

## Step 7

Implement Android H.264 encoder.

Verify encoded frames locally/logically before networking them.

## Step 8

Transport VIDEO_CONFIG and VIDEO_FRAME.

Measure throughput.

## Step 9

Implement Windows Media Foundation decoder.

## Step 10

Render decoded video in Windows.

At this point, stop and stabilize the MVP before starting virtual camera work.

---

# 46. MVP Completion Gate

Do **not** begin the virtual-camera milestone until all conditions are true:

```text
[ ] Android preview stable
[ ] Android H.264 encoder stable
[ ] Windows connection stable
[ ] Protocol parser tested
[ ] Windows H.264 decode stable
[ ] Windows preview works
[ ] 720p / 30 FPS achieved
[ ] reconnect works
[ ] 30-minute streaming test passes
[ ] memory does not grow continuously
[ ] latency is measured
```

---

# 47. Virtual Camera Completion Gate

Do not begin USB/audio/advanced features until:

```text
[ ] WebCamKu Camera registers
[ ] WebCamKu Camera unregisters cleanly
[ ] consuming application can open it
[ ] phone frames appear in the virtual camera
[ ] disconnect does not crash consumer
[ ] reconnect resumes frames
[ ] Windows client can restart cleanly
```

---

# 48. Current Project Checklist

## M0 — Bootstrap

- [x] repository created
- [x] Android project builds
- [x] Windows solution builds
- [x] protocol directory created
- [x] docs skeleton created

## M0.1 — Android camera

- [ ] camera permission
- [ ] CameraX preview
- [ ] front/back camera

## M0.2 — Protocol

- [ ] WKC/1 header
- [ ] parser
- [ ] writer
- [ ] HELLO
- [ ] HELLO_ACK
- [ ] PING/PONG
- [ ] tests

## M0.3 — H.264 encoder

- [ ] MediaCodec AVC
- [ ] encoder Surface
- [ ] SPS/PPS extraction
- [ ] keyframe detection
- [ ] timestamps

## M0.4 — Wi-Fi transport

- [ ] Android TCP server
- [ ] Windows TCP connection
- [ ] VIDEO_CONFIG
- [ ] VIDEO_FRAME

## M0.5 — Windows preview

- [ ] Media Foundation decoder
- [ ] frame output
- [ ] WPF preview
- [ ] FPS
- [ ] bitrate
- [ ] dropped frames
- [ ] reconnect

## M0.6 — Controls

- [ ] switch camera
- [ ] torch
- [ ] zoom

## M0.7 — Robustness

- [ ] bounded queues
- [ ] timeout
- [ ] reconnect
- [ ] recovery
- [ ] 30-minute test

## M0.8 — Windows 11 virtual camera

- [ ] media source
- [ ] MF virtual camera registration
- [ ] shared frame provider
- [ ] test consumer
- [ ] install/uninstall flow

## M0.9 — USB

- [ ] detect ADB
- [ ] detect phone
- [ ] configure port forwarding
- [ ] USB streaming

## Later

- [ ] 1080p
- [ ] discovery
- [ ] audio
- [ ] installer
- [ ] signed release

---

# 49. Known Technical Risks

## Android device differences

Camera and codec implementations vary by manufacturer.

Mitigation:

- CameraX;
- MediaCodec capability checks;
- fallbacks;
- physical-device test matrix.

## Wi-Fi latency/jitter

TCP can create latency when packet loss occurs.

Mitigation:

- keep buffers small;
- monitor latency;
- stabilize TCP MVP first;
- evaluate RTP/UDP or WebRTC only after real measurements justify it.

## Windows Media Foundation complexity

COM and media source lifecycle are easy to get wrong.

Mitigation:

- isolate native code;
- follow Microsoft virtual-camera sample patterns;
- keep a minimal interface to managed code.

## Virtual-camera compatibility

Different consumers may request different formats/resolutions.

Mitigation:

- begin with a small known format set;
- test with multiple consumers;
- add format negotiation gradually.

---

# 50. Future Architecture Options

Only evaluate these after V1 core stability:

```text
RTP/UDP transport
WebRTC transport
H.265
AV1
4K
60 FPS
Wi-Fi Direct
hardware-specific camera controls
multi-camera
remote Internet relay
Linux client
macOS client
iOS client
```

Every addition must preserve a clean abstraction around:

```text
Capture
Encode
Transport
Decode
Frame Sink
```

---

# 51. Official Technical References

Use primary documentation as the source of truth.

Android CameraX:

https://developer.android.com/media/camera/camerax

CameraX video architecture:

https://developer.android.com/media/camera/camerax/video-capture

Android MediaCodec:

https://developer.android.com/reference/android/media/MediaCodec

Android supported media formats:

https://developer.android.com/media/platform/supported-formats

Android Debug Bridge:

https://developer.android.com/tools/adb

Windows MFCreateVirtualCamera:

https://learn.microsoft.com/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera

Windows IMFVirtualCamera:

https://learn.microsoft.com/windows/win32/api/mfvirtualcamera/nn-mfvirtualcamera-imfvirtualcamera

Microsoft Windows-Camera VirtualCamera sample:

https://github.com/microsoft/Windows-Camera/tree/master/Samples/VirtualCamera

When documentation and assumptions conflict, follow current platform documentation and record the design change.

---

# 52. Final Product Principle

The engineering priority is:

```text
STABILITY
    ↓
LOW LATENCY
    ↓
IMAGE QUALITY
    ↓
EASE OF USE
    ↓
ADVANCED FEATURES
```

Do not trade basic stability for feature count.

The first success criterion is not:

> "WebCamKu has many features."

It is:

> "I open the Android app, connect from Windows, and the phone camera becomes a stable low-latency Windows webcam."

---

# 53. Start Here

The immediate Codex task is:

```text
Implement M0 only.

1. Create the monorepo structure defined in PROJECT.md.
2. Scaffold the Kotlin + Jetpack Compose Android application.
3. Scaffold the .NET 10 WPF Windows application.
4. Create protocol/protocol.md containing the initial WKC/1 framing specification.
5. Create docs skeleton files.
6. Ensure both Android and Windows projects build.
7. Do not implement video streaming yet.
8. Update the M0 checklist after successful builds.
```

After M0 passes, continue to **M0.1 — Android Camera Preview**.

Do not skip milestone acceptance gates.
