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

**Immersive: the same saved connection, explicit fullscreen and Apple Allow,
one owned PC application, and reliable return to the desktop.**

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

Not yet verified: any latency figure. The simulator build stubs the Apple
streaming session and is for UI checks only; it is not a hardware result.

Each step must leave a usable product.
