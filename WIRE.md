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
| 0 VIDEO | host → headset | `u64 LE captureTimestampUs`, `u8 flags` (bit 0 = keyframe, bit 1 = repeat of the previous frame), one H.264 Annex B access unit; SPS and PPS precede every IDR |
| 1 CONTROL | both | UTF-8 JSON object with `v` and `type` |

Frames above 16 MiB drop the connection on both sides.

### Control, v1

| `type` | direction | fields | meaning |
| --- | --- | --- | --- |
| `hello` | headset → host | `token` | must be the first frame; the host compares SHA-256(token) to the pair record in constant time and drops the connection on mismatch, before any video |
| `stream` | host → headset | `width`, `height`, `fps` | reply to `hello`; video follows, starting with an IDR |
| `keyframe` | headset → host | | send an IDR as soon as possible |
| `bye` | both | | orderly close |

## Input

The desktop TLS connection is the control lease. When it closes for any
reason, the host releases every held button and key. There is no other lease,
heartbeat or capability negotiation.

| type | direction | payload |
| --- | --- | --- |
| 2 INPUT | headset → host | one 16-byte record: `u8 kind`, `u8 flags`, `u16 reserved = 0`, `i32 LE a`, `i32 LE b`, `i32 LE c` |

| kind | flags | a / b / c |
| --- | --- | --- |
| 1 move | 0 | x / y / 0, normalized 0..65535 over the streamed frame, origin top-left |
| 2 button | bit 0 = down | x / y / button (1 left, 2 right, 3 middle) |
| 3 wheel | 0 | vertical / horizontal / 0, Windows wheel units (120 per notch), positive vertical = away from the user |
| 4 key | bit 0 = down | USB HID keyboard-page usage / 0 / 0; modifiers are ordinary key records |
| 5 text | 0 | Unicode scalar / 0 / 0, committed text; never both a key and a text record for one keystroke |

The host drops the connection on an unknown kind, nonzero reserved, an
out-of-range value or a record that is not 16 bytes. Adjacent moves may be
coalesced; nothing is reordered across a button, key or wheel. The headset
sends at most one move per displayed frame. Neither side logs coordinates,
keys or text.

## Immersive

Fullscreen reuses the saved pair. Nothing new is trusted.

| `type` | direction | fields | meaning |
| --- | --- | --- | --- |
| `immersive` | headset → host | | start immersive: the host pauses window video, starts the CloudXR service and its own OpenXR session drawing the desktop on a quad, then replies |
| `immersive` | host → headset | `port` | the Apple session-management TCP port; the headset connects its Foveated Streaming session there with the saved ClientID and the host acknowledges with the fingerprint, sends no `paired`, and proceeds to `MediaStreamIsReady` |
| `windowed` | both | | end immersive: the host ends its OpenXR session, stops the service, resumes window video with an IDR and replies `windowed`; the headset disconnects its Apple session |

An Apple session that reports `DISCONNECTED` ends immersive as if `windowed`
had arrived. If the headset's Apple connect fails it sends `windowed` at once.
The desktop TLS stream stays open throughout and keeps carrying INPUT records;
the desktop window stays open as the input surface. CloudXR media is not
encrypted by the vendor; the desktop stream is.

## Games

A game runs inside the immersive session: the host ends its own quad session
and launches one owned game process whose OpenXR session takes over the same
stream. No new connection, port or trust.

| `type` | direction | fields | meaning |
| --- | --- | --- | --- |
| `games` | host → headset | `games`: list of `{id, name}` | sent once after `stream`; what the host found installed, possibly empty |
| `game` | headset → host | `id` | start this game; only while immersive |
| `game` | host → headset | `id`, `running`, optional `reason` | `running: true` once the process is up; `running: false` when it exits or fails, after which the host's quad session is back |

| `recenter` | headset → host | | while a game runs: the host sends the game's recenter chord to its window (Assetto Corsa: Ctrl+Space); no reply |

`windowed` while a game runs kills the game first, then returns as usual.
