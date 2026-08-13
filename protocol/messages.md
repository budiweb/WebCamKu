# WKC/1 Messages

The authoritative type registry and framing rules are in [protocol.md](protocol.md).

`HELLO` is UTF-8 JSON sent by Android. It requires a non-null string `deviceName`, string `appVersion`, integer `protocolVersion` equal to 1, a `cameras` array, and a `video` object when those capabilities are available. Receivers must ignore unknown JSON properties and reject invalid JSON or unsupported protocol versions.

`HELLO_ACK` is UTF-8 JSON sent by Windows. It requires boolean `accepted`, integer `protocolVersion` equal to 1, and an optional string `reason` when rejected.

`PING` and `PONG` have empty payloads. A `PONG` echoes the corresponding ping timestamp and allows implementations to correlate it by sequence number. No socket behavior is implemented until M0.4.
