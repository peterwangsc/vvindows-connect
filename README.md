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

**Input: pointer, physical and virtual keyboard, balanced releases on
disconnect, verified in the real windowed product.**

Verified on hardware on 2026-09-16:

- Pair once through both apps: Windows Pair, Vision Pro Pair, select the PC,
  scan the QR, no PC click. Restart keeps the pair; Forget clears it on both
  sides.
- Windowed desktop: Connect opens the Windows desktop in a window over a pinned
  TLS 1.3 connection whose credentials arrived over the Apple-authenticated
  pairing session. Motion is visible, Disconnect and reconnect work.

Not yet verified: reconnecting the Apple session without a second scan (only
needed for immersive), and any latency figure.

Immersive PC games follow, through the same applications. Each step must leave
a usable product.
