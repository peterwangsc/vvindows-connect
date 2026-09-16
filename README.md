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

**Windowed desktop: authenticated connection, one video path, display,
disconnect and reconnect.**

Milestone 1 was verified on hardware on 2026-09-16: Pair on Windows, Pair on
Vision Pro, select the PC, scan the QR once, no PC click. The headset stored
the pair, showed it again after relaunch, and Forget cleared it on both sides.
The host log showed WAITING to `paired` delivered in under three seconds.

Not yet verified: reconnecting without a second scan. That needs the Connect
action, which arrives with the windowed desktop. A simulator result is not an
Apple system-framework hardware result.

Keyboard/mouse and immersive PC games follow, through the same applications.
Each step must leave a usable product.
