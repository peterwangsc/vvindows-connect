# Windows app

C# native desktop application. The app owns the host session directly.

The home screen contains connection status, **Pair**, and Settings. Pair
shows the QR in the same window and authorizes one authenticated enrollment.
No PIN, second approval, device list or Windows-side Connect button.

## Layout

- `VindOS/` — WPF app (`net10.0-windows`). `Host.cs` owns the whole session:
  Bonjour advertisement (`_apple-foveated-streaming._tcp`, TXT
  `Application-Identifier=com.golfcore.vvindowsconnect`), the Apple
  session-management TCP protocol, the NVIDIA Stream Manager process and RPC,
  the pair record, and the in-process OpenXR session.
- `VindOS.Xr/` — one C++ DLL. It runs the OpenXR session on the CloudXR
  runtime (D3D11, blank stereo frames) and sends the `paired` message over the
  opaque data channel once the session is visible and the channel connects.
  Built to `VindOS/native/` and copied next to the app.
- `vendor/` — not in Git. NVIDIA CloudXR binaries, placed by hand:
  - `vendor/NvStreamManagerClient.dll` from Stream Manager 6.1.0 `SampleClient/`
  - `vendor/Server/{NvStreamManager.exe,CloudXrService.exe,cloudxr-runtime.yaml}` from Stream Manager 6.1.0 `Server/`
  - `vendor/Server/releases/6.2.3/` — the contents of CloudXR Runtime 6.2.3 Win64
  - `vendor/opencomposite/openvr_api.dll` — OpenComposite x64, openxr branch (znix.xyz/OpenComposite), sha256 827ad85f…08242c

## Build

`build.cmd` builds the native module with Visual Studio 2022 MSBuild (v143)
and then the app with the .NET 10 SDK. Output:
`VindOS/bin/Release/net10.0-windows/win-x64/vindOS.exe`.

## Run

Run `vindOS.exe` as a normal user. The app refuses to start elevated because
the OpenXR loader ignores the app's runtime selection (`XR_RUNTIME_JSON`) in a
high-integrity process and falls back to the machine-wide ActiveRuntime, which
this app never writes.

State lives in `%LOCALAPPDATA%\vindOS\`: `identity.bin` (ServerID) and
`pair.bin` (the single pair record), both DPAPI current-user; `log.txt` holds
bounded session-state and disconnect-code lines and never a token or QR.

## Pairing flow

1. Pair arms one enrollment. The next `RequestConnection` from an unknown
   ClientID is acknowledged without a certificate fingerprint, so Vision Pro
   asks for the barcode; the QR (`{"token","digest"}` from the Stream
   Manager) is shown in the window.
2. On `WAITING` the app starts the CloudXR service, starts its own OpenXR
   session and frame loop, then sends `MediaStreamIsReady`.
3. When the session is visible and the data channel connects, the module sends
   `{"v":1,"type":"paired","serverId","hostName"}` (see `../WIRE.md`).
4. On `CONNECTED` after a scan the pair record is saved. A later
   `RequestConnection` from the saved ClientID is acknowledged with the
   fingerprint so Vision Pro reconnects without a scan.
5. Forget deletes the record and disconnects; any other ClientID is refused
   until Pair is clicked again.

## Desktop stream (WIRE.md v2)

`Desktop.cs` listens on `DesktopPort`, advertised in the Bonjour TXT record
next to `ServerID`. Each connection is TLS 1.3 with ALPN `vindos/1` and the
host's self-signed certificate; the headset pins its SHA-256, which travelled in
`paired`. The first frame must be `hello` with the desktop token; the host
compares SHA-256(token) to the pair record in constant time and drops anything
else. On success it replies `stream`, starts capture, and sends video frames
(u32 LE length, type 0, u64 LE timestamp in microseconds, flags, Annex B).
`keyframe` forces an IDR; `bye` or a closed socket stops capture. A later
authenticated connection replaces the earlier one. Frames queue three deep;
when the link falls behind, the oldest frame is dropped and an IDR requested.

Measured on this PC (RTX 4070, 1920x1080, animated window, loopback TLS):
59.0 fps delivered, first frame an IDR with in-band SPS/PPS, wrong token
refused before any video.

## Input (WIRE.md v3)

`Input.cs` applies the 16-byte INPUT records from the desktop connection with
`SendInput`: moves and buttons map the normalized point onto the captured
output's rectangle as absolute virtual-desktop coordinates, wheel units pass
through, keys map USB HID usages to set-1 scan codes, text injects Unicode
units. Every held button and key is released when the connection ends for
any reason. A malformed record drops the connection.

vindOS controls apps running as a normal user; windows running as
administrator ignore its input (Windows UIPI, because vindOS itself must run
non-elevated for the OpenXR runtime selection). An app launched from an
elevated terminal inherits that elevation, which is the usual way this shows
up.

Verified on hardware 2026-09-16: click, drag and virtual-keyboard text in
Notepad. Wheel and physical-keyboard records still pending a headset run.

## Immersive (WIRE.md v4)

`immersive` on the desktop stream stops the window encoder (Windows allows
one Desktop Duplication per output per process), starts the CloudXR service
and the host's own OpenXR session in desktop-quad mode, and replies
`immersive{port}` with the Apple session-management port. `xr.cpp` copies the
latest captured frame into a quad swapchain every rendered frame and submits
it as a world-locked XR_COMPOSITION_LAYER_QUAD, 2.4 m wide at 2.0 m, behind a
black projection layer. The headset then connects its Foveated Streaming
session with the saved ClientID; Apple skips the QR and the host sends no
`paired`. `windowed` from either side, or the Apple session ending, tears the
session down, restarts the encoder with an IDR and replies `windowed`; the
service stop runs on a background thread so the reply lands within ~300 ms.
The two teardown triggers arrive within milliseconds of each other and are
serialized by one lifecycle semaphore.

Verified on hardware 2026-09-16: ten entries, each CONNECTED about 2.4 s after
Fullscreen with no QR; return in 270 ms with the desktop stream intact; the
desktop is visible on the quad. CloudXR media is not encrypted by the vendor;
only its signaling and the desktop stream are.

## Games

Games are started from Steam, the way Peter already starts them. vindOS does
not launch games and shows no Play list; the headset shows none either.

Steam must carry the runtime. "Restart Steam through vindOS" shuts Steam down
(`steam.exe -shutdown`, measured 2 s), then starts it with `XR_RUNTIME_JSON`
set to the CloudXR runtime json. Every game Steam launches inherits that
environment: measured 2026-09-16 15:31 with a marker variable, present in
`steam.exe`, every `steamwebhelper.exe`, and `AssettoCorsa.exe` launched by
`steam://rungameid/244210`, which also carried `SteamAppId=244210`. The status
line under the button reads `steam.exe`'s environment block (`ProcessEnv.cs`,
PEB → RTL_USER_PROCESS_PARAMETERS) and says whether Steam currently runs
through vindOS; it is not remembered anywhere else.

`Steam.cs` scans the library on startup and on "Rescan Steam library": every
`appmanifest_*.acf` across `libraryfolders.vdf`, joined to the binary
`appcache/appinfo.vdf` (format 0x07564429, string-table keys). A title is a VR
game when its `common` block carries `openvrsupport` or `openxrsupport`, or a
launch entry is typed `vr` or `openxr`. The list is shown in Settings with each
title's bridge state.

OpenVR titles need the OpenComposite bridge: "Apply OpenVR bridges" copies
`vendor/opencomposite/openvr_api.dll` over every 64-bit `openvr_api.dll` under
each OpenVR title's install directory, keeping the original beside it as
`openvr_api.dll.vindos-original`; "Remove OpenVR bridges" puts the originals
back and deletes the copies. OpenXR titles need nothing. Nothing touches game
files without one of those two clicks.

While the headset is in Immersive Mode the quad shows the desktop and Peter
presses Play in Steam on it. `VrWatch.cs` polls every 500 ms for a process
other than vindOS that has `openvr_api.dll` or `openxr_loader.dll` loaded;
when one appears the host disposes its quad session so the game's OpenXR
session can attach, logs the process, its `SteamAppId` and whether it
inherited the vindOS runtime, and sends `game{id,running:true}` to the
headset. When that process exits the quad session restarts and
`game{id,running:false}` follows. `recenter` sends Ctrl+Space when that
process owns the foreground window, otherwise re-places the quad. `windowed`
leaves the game running on the PC. Games started from Steam while windowed run
flat, and a VR launch then fails the same way it would with no runtime
installed.

Measured 2026-09-16 15:13–15:19 with the earlier host-launched variant: the
Stream Manager's `AppConnected` flag is set by the host's own quad session, so
it cannot signal a game attaching while the quad is up; the module poll is the
signal. Hand-off on that run: `acs.exe` loaded the bridge, the quad yielded
74 ms later, `AppConnected` dropped and returned within 0.5 s as Assetto's
session attached, the headset client stayed connected throughout.
