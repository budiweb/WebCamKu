# Architecture

WebCamKu is a monorepo with an Android capture application, a Windows WPF client, shared protocol documentation, and later isolated native video components. Capture, encoding, transport, decoding, and frame sinks remain separate concepts; only the milestone-approved components are implemented.

As of M0.3, Android camera preview and H.264 encoding remain separate lifecycle modes. CameraX owns the normal preview; the local encoder diagnostic releases CameraX and uses a Camera2 recording request targeting the `MediaCodec` input surface. This smallest device-compatible design proves encoding independently of networking. A later streaming milestone will integrate preview and encoding without moving transport logic into either camera component.
