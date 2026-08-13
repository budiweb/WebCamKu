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

Control payloads are UTF-8 JSON. Encoded video is binary and must never be Base64-wrapped in JSON. The maximum payload length is **16 MiB (16,777,216 bytes)**. Implementations must reject invalid magic, unsupported versions, unknown message types, invalid JSON, and excessive lengths, and must correctly handle partial and concatenated TCP reads.

Flags use bits `0x0001` for `KEY_FRAME`, `0x0002` for `CONFIG`, and `0x0004` for `END_OF_STREAM`; all other bits are reserved and must be zero in WKC/1. Sequence numbers are unsigned 32-bit values assigned by each sender and incremented for every packet, wrapping after `0xFFFFFFFF`. Incompatible semantic changes require WKC/2.
