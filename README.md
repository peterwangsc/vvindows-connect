# vindOS

**Your Windows PC on Apple Vision Pro. Free and open source.**

Use your Windows desktop in a window, move into an immersive screen, and
play supported Steam VR games on your headset. No vindOS subscription,
paid unlocks, or account required.

vindOS is for people who want an alternative to paid desktop-streaming apps.
It is an early release with a small tested hardware and game set; it does
not promise feature parity or compatibility with every PC or VR game.

[Website](https://www.peterwang.tech/vindos) ·
[Support](https://www.peterwang.tech/vindos/support) ·
[MIT license](LICENSE)

## Availability

**Pre-release 0.1.0.** The Windows pre-release is linked from the website.
It is unsigned and Windows may show an unknown-publisher warning. The
Vision Pro app has been uploaded to TestFlight; a public invitation is not
available yet. Public app launch is pending the items in [LAUNCH.md](LAUNCH.md).

The source is available here for both apps. This repository was originally
named VVindows Connect; the product is **vindOS**.

## Requirements

- Apple Vision Pro running visionOS 26.4 or later.
- Windows 11 x64 with an NVIDIA GPU. Hardware testing so far used an RTX 4070.
- Both devices on the same trusted local network.
- NVIDIA CloudXR Runtime **6.2.3** and Stream Manager **6.1.0**, downloaded
  separately from [NVIDIA NGC](https://catalog.ngc.nvidia.com/).
  NVIDIA account access and NVIDIA's terms apply separately. The current
  app needs these components for setup and pairing, including windowed use.

vindOS does not bundle NVIDIA software. On Windows, place the two downloaded
ZIP archives in `%LOCALAPPDATA%\vindOS\CloudXR` and click **Check again**.
See [support](https://www.peterwang.tech/vindos/support) for setup details.

## Use it

1. Click **Pair** on Windows, then **Pair** on Vision Pro and select the PC.
2. Scan the Windows QR code and accept Apple's permission prompt.
3. Click **Connect** to open your desktop. Pairing does not connect automatically.
4. Use fullscreen for **Immersive Mode**; use **Windowed** to return.
5. For VR games, restart Steam through vindOS in Windows Settings, then
   launch the game from Steam. OpenVR titles need the OpenComposite bridge;
   Settings can apply and restore it. OpenXR titles need no bridge.

There is one saved connection. **Forget connection** in Settings clears it.
Game bridge changes keep the original DLL beside the replacement; removing
the bridge or uninstalling vindOS restores the original.

## What has been tested

Earlier hardware runs on **2026-09-16** verified pairing, saved reconnect,
Forget, live windowed desktop, pointer/click/drag/scroll, physical and visionOS
keyboard input, immersive entry and return, and Steam handoff to and from
**Assetto Corsa** using the PC's keyboard and mouse. These observations do
not certify every later commit or a new release package.

Known limits:

- Windows running as administrator do not accept vindOS input. Run vindOS
  as a normal user.
- Mac Virtual Display's keyboard path is unavailable while its display is
  hidden by an immersive space.
- The windowed desktop uses pinned TLS 1.3. CloudXR immersive media is not
  encrypted by the vendor; use a trusted network. See [SECURITY.md](SECURITY.md).
- No end-to-end latency figure has been measured. Other GPUs and games have
  not been established as supported by our hardware tests.

## Build and contribute

Two native apps: Swift/SwiftUI on Apple, C#/WPF on Windows, with one C++ DLL
for Windows graphics and OpenXR. No hosted vindOS service is required.

- [Apple build instructions](apple/README.md)
- [Windows build instructions](windows/README.md)
- [Architecture](ARCHITECTURE.md) and [wire format](WIRE.md)
- [Contributing](CONTRIBUTING.md)

## Current milestone

**Free, open-source launch of v0.** Finish public distribution, licensing
notices, and a complete install-to-connect check through both actual apps
before adding another feature. [LAUNCH.md](LAUNCH.md) tracks readiness and
contains announcement copy.

## License and privacy

vindOS source is [MIT-licensed](LICENSE). Third-party components retain their
own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). MIT permits
commercial forks; the official vindOS app is intended to remain free.

vindOS has no app analytics, advertising, or subscription service. Pairing
credentials stay in Apple Keychain and Windows current-user DPAPI storage.
Windows keeps a local diagnostic log that can contain device names, network
addresses, game/process names, and file paths. Review it before sharing.
See the [privacy policy](https://www.peterwang.tech/vindos/privacy).