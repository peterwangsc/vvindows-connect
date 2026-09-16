# Wire

Everything between the two apps rides on Apple's Foveated Streaming session.
The session-management protocol, mDNS service, QR and trust store are Apple's;
see "Establishing foveated streaming sessions with Apple Vision Pro". This file
holds only what the two apps add on top.

Identity the host advertises: `_apple-foveated-streaming._tcp`, TXT
`Application-Identifier=com.golfcore.vvindowsconnect`.

## Message channel

The host opens one `FoveatedStreamingSession.MessageChannel` once the session is
connected. Every message is one UTF-8 JSON object with `v` and `type`.

### `paired` (host → headset), v1

Sent once, immediately after the channel opens. The headset stores it, then
disconnects. `hostName` is the Bonjour instance name the host advertises.

```json
{"v":1,"type":"paired","serverId":"5d542eab102b46caa5801cf3f9f33c3a","hostName":"THUNDERBONE"}
```
