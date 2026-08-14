# WKC/1 Messages

The authoritative type registry and framing rules are in [protocol.md](protocol.md).

`HELLO` is UTF-8 JSON sent by Android. It requires a non-null string `deviceName`, string `appVersion`, integer `protocolVersion` equal to 1, a `cameras` array, and a `video` object when those capabilities are available. Receivers must ignore unknown JSON properties and reject invalid JSON or unsupported protocol versions.

`HELLO_ACK` is UTF-8 JSON sent by Windows. It requires boolean `accepted`, integer `protocolVersion` equal to 1, and an optional string `reason` when rejected.

`PING` and `PONG` have empty payloads. A `PONG` echoes the corresponding ping timestamp and allows implementations to correlate it by sequence number. No socket behavior is implemented until M0.4.

`COMMAND` is UTF-8 JSON sent by Windows. It requires a non-empty string `commandId`, a
string `name`, and a command-specific `value`:

```json
{"commandId":"42","name":"switchCamera"}
{"commandId":"43","name":"torch","value":true}
{"commandId":"44","name":"zoom","value":2.0}
```

M0.6 command names are exactly `switchCamera`, `torch`, and `zoom`. Zoom is a finite
ratio greater than or equal to 1. Android clamps it to the camera's supported range.

Every valid `COMMAND` receives one `COMMAND_ACK` with the same `commandId`:

```json
{"commandId":"42","success":true,"state":{"facing":"front"}}
{"commandId":"43","success":false,"error":"Torch is not supported by this camera"}
```

Malformed JSON, unknown commands, invalid values, and unsupported hardware return a
clean unsuccessful ACK. They do not close the connection. Protocol-level failures that
cannot be correlated with a command may use `ERROR`.
