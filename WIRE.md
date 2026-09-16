# Wire

Everything between the two apps starts on Apple's Foveated Streaming session.
The session-management protocol, mDNS service, QR and trust store are Apple's;
see "Establishing foveated streaming sessions with Apple Vision Pro". This file
holds only what the two apps add on top.

## Bonjour

Service `_apple-foveated-streaming._tcp`, instance name = the PC's machine
name. TXT keys:

| Key | Value |
| --- | --- |
| `Application-Identifier` | `com.golfcore.vvindowsconnect` |
| `ServerID` | the host's ServerID, the same value sent in `AcknowledgeConnection` |
| `DesktopPort` | TCP port of the desktop stream, chosen once per PC |

The headset finds the PC by matching `ServerID` against its saved pair.

## Pairing message channel

The host opens one `FoveatedStreamingSession.MessageChannel` once the session
is connected and sends one UTF-8 JSON object. The headset stores it and
disconnects.

### `paired` (host → headset), v2

```json
{"v":2,"type":"paired","serverId":"5d542eab102b46caa5801cf3f9f33c3a","hostName":"THUNDERBONE",
 "desktop":{"sha256":"<hex SHA-256 of the host's desktop TLS leaf certificate, DER>",
            "token":"<64 hex characters, CSPRNG, unique per ClientID>"}}
```

The host stores SHA-256(token) in its pair record, never the token. The headset
stores `sha256` and `token` in the Keychain. Forget on either side deletes its
copy. Keep the message small; the channel has fragmented larger payloads.

## Desktop stream

TCP to `DesktopPort`, TLS 1.3, ALPN `vindos/1`. The host presents its desktop
certificate only; the headset accepts exactly the pinned leaf SHA-256, with no
CA and no hostname check. The host serves one connection; a later authenticated
connection replaces the earlier one.

Every frame in either direction: `u32 LE length` of what follows, `u8 type`,
payload.

| type | direction | payload |
| --- | --- | --- |
| 0 VIDEO | host → headset | `u64 LE captureTimestampUs`, `u8 flags` (bit 0 = keyframe), one H.264 Annex B access unit; SPS and PPS precede every IDR |
| 1 CONTROL | both | UTF-8 JSON object with `v` and `type` |

Frames above 16 MiB drop the connection on both sides.

### Control, v1

| `type` | direction | fields | meaning |
| --- | --- | --- | --- |
| `hello` | headset → host | `token` | must be the first frame; the host compares SHA-256(token) to the pair record in constant time and drops the connection on mismatch, before any video |
| `stream` | host → headset | `width`, `height`, `fps` | reply to `hello`; video follows, starting with an IDR |
| `keyframe` | headset → host | | send an IDR as soon as possible |
| `bye` | both | | orderly close |
