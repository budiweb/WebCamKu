# WKC/1 — WebCamKu Camera Protocol v1

Status: initial M0 specification. No protocol implementation exists yet.

WKC/1 uses a TCP byte stream. Every message consists of a fixed 24-byte header followed immediately by its payload. Multi-byte integers are unsigned and encoded in network byte order (big-endian).

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | Magic: ASCII `WKC1` |
| 4 | 1 | Version: `1` |
| 5 | 1 | Message type |
| 6 | 2 | Flags |
| 8 | 4 | Payload length |
| 12 | 8 | Timestamp in microseconds |
| 20 | 4 | Sequence number |

Initial message types are `HELLO` (`0x01`), `HELLO_ACK` (`0x02`), `VIDEO_CONFIG` (`0x03`), `VIDEO_FRAME` (`0x04`), `COMMAND` (`0x05`), `COMMAND_ACK` (`0x06`), `PING` (`0x07`), `PONG` (`0x08`), `STATS` (`0x09`), `ERROR` (`0x0A`), `STREAM_START` (`0x0B`), and `STREAM_STOP` (`0x0C`).

Control payloads are UTF-8 JSON. Encoded video is binary and must never be Base64-wrapped in JSON. Implementations must enforce a strict payload limit, reject invalid magic or unsupported versions, and correctly handle partial and concatenated TCP reads. Exact limits and flag values will be fixed when the parser is implemented in M0.2; incompatible semantic changes require WKC/2.

