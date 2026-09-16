# VVindows Connect

Your Windows PC on Apple Vision Pro.

Private, greenfield monorepo. Swift/SwiftUI on Apple; C# on Windows.
This repository starts from zero. No Spatial PC source, build artifacts,
credentials, or protocol compatibility are carried forward.

## Product

One headset, one PC, one saved connection.

1. Click **Pair** on Windows. Click **Pair** on Vision Pro and select the PC.
2. Scan the Windows QR code and accept the required Apple permission.
3. Pairing finishes at **Connect**. Nothing opens automatically.
4. **Connect** opens the desktop in a window.
5. Fullscreen requests **Immersive Mode** using the saved pairing.

No PIN, second PC approval, device list, or setup instructions filling the UI.
Settings contains **Forget connection**. Pair again to change devices.
Platform-owned permission UI remains platform-owned.

## Layout

- `apple/` — the visionOS app.
- `windows/` — the Windows app.
- [ARCHITECTURE.md](ARCHITECTURE.md) — boundaries and implementation sequence.

## Current milestone

**Soft v0.** vindOS 0.1.0 (1) for Vision Pro is uploaded to App Store Connect
for TestFlight. The Windows installer (per-user, unsigned, ships no NVIDIA
files; the app imports the CloudXR Runtime and Stream Manager archives the user
downloads from NGC) is built from `windows/`.

Verified on hardware on 2026-09-16:

- Pair once through both apps: Windows Pair, Vision Pro Pair, select the PC,
  scan the QR, no PC click. Restart keeps the pair; Forget clears it on both
  sides; a later pair with the same PC skips the QR.
- Windowed desktop: Connect opens the Windows desktop in a window over a pinned
  TLS 1.3 connection whose credentials arrived over the Apple-authenticated
  pairing session. Motion is visible, Disconnect and reconnect work.
- Input: pointer, click, drag, trackpad scroll, physical keyboard and the
  visionOS keyboard all reach Windows. Windows running as administrator ignore
  injected input (UIPI); vindOS runs as a normal user.
- Fullscreen: the desktop on a large screen placed in front of the viewer;
  the Crown dials the surroundings; a pinch on the surroundings shows a panel
  with Recenter and Windowed, another hides it. Windowed, Apple's Exit, the
  Crown and the Home gesture all return to the live desktop window.
- Games: Steam runs through vindOS once restarted from Settings; Play inside
  Steam on the big screen hands the screen to the game, which recenters once
  on take-over; quitting returns the desktop (Assetto Corsa, driven with the
  PC's keyboard and mouse).

Decided for v0: NVIDIA CloudXR is not bundled (the user downloads it from NGC
and vindOS imports it); the installer is unsigned and says so; the product is
vindOS; the installer carries a pre-release license. Before a public link:
revisit CloudXR redistribution and buy a code-signing certificate. The MacBook
keyboard cannot reach
the headset in Fullscreen because Mac Virtual Display is hidden inside
immersive spaces. No latency figure has been measured.
