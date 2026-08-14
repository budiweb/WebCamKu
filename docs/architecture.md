# Architecture

WebCamKu is a monorepo with an Android capture application, a Windows WPF client, shared protocol documentation, and later isolated native video components. Capture, encoding, transport, decoding, and frame sinks remain separate concepts; only the milestone-approved components are implemented.

As of M0.3, Android camera preview and H.264 encoding remain separate lifecycle modes. CameraX owns the normal preview; the local encoder diagnostic releases CameraX and uses a Camera2 recording request targeting the `MediaCodec` input surface. This smallest device-compatible design proves encoding independently of networking. A later streaming milestone will integrate preview and encoding without moving transport logic into either camera component.

As of M0.5, the Windows pipeline separates the async socket reader, bounded encoded-packet queue, native Media Foundation decoder, decoded-frame callback, and WPF renderer. Media Foundation and COM are isolated in `WebCamKu.Video`; the client sees only BGRA frames. The current CPU NV12-to-BGRA conversion is correct and stable for the MVP, while the boundary permits a lower-copy renderer later.

As of M0.6, control is duplex on the existing TCP session. Android reads `COMMAND`
packets on the session thread while a dedicated sender drains the bounded encoded-video
queue. Writes are serialized so ACK and video packets cannot interleave. Camera switch
reopens only the Camera2 capture device/session around the existing encoder surface;
torch and zoom update the repeating capture request without restarting the encoder.

As of M0.7, the Windows UI owns an automatic reconnect loop with bounded backoff of
1, 2, 3, then 5 seconds. Each attempt constructs fresh socket and decoder resources;
Disconnect cancels both an active receive and a pending retry. Connect and packet
inactivity have 10-second timeouts. The decoder can flush/reset after an input failure
and primes itself again with the latest codec configuration. Android stops streaming
resources when its activity stops and schedules a bounded one-at-a-time encoder/camera
restart after pipeline errors.

As of M0.8, decoded BGRA frames are published to a versioned file-backed shared-memory
surface before WPF repaint scheduling. The Windows 11 Media Foundation virtual-camera
source reads the newest coherent frame with a sequence lock, retains the last valid frame
during writer collisions or disconnects, and exposes NV12/RGB32 at 1280x720/30 FPS.
Registration and the persistent privileged host are separate from the WPF process, so
ordinary start/stop controls do not require repeated elevation.

As of M0.9, connection mode is chosen at the WPF transport boundary. Wi-Fi uses the
entered LAN address; USB locates ADB, validates one authorized device, installs a scoped
TCP 4747 forward, and connects the unchanged WKC/1 transport to localhost. Forwarding is
re-established during reconnect and removed on disconnect, so capture, protocol,
decoder, preview, controls, and virtual-camera sinks are transport-independent.

As of M0.10, the Android streaming Camera2 session targets both the encoder input and a
local preview surface. Brightness and focus controls remain local UI concerns while the
camera component owns repeating-request changes. On Windows, `webcamku-obs.dll` is an
independent asynchronous OBS source sink: it reads the file-backed latest BGRA frame by
sequence lock and never adds another network/decode queue.

Because the M0.10 preview starts capture before a Windows connection, Android retains
the latest codec configuration and sends it explicitly to each new client, then requests
a fresh keyframe. The Windows latest-frame decode queue treats codec configuration as
priority state rather than a disposable video frame, preventing SPS/PPS from being
overwritten by the first access unit.
