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
