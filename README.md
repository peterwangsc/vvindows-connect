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

**Pair once through the actual apps, persist the connection, and reopen it.**

This initial commit contains the design and repository structure only. There
is no application implementation or working-feature claim yet. Before moving
to desktop streaming, the real app flow must finish pairing, survive restart,
and forget the connection correctly. A simulator result is not an Apple
system-framework hardware result.

Windowed desktop, keyboard/mouse, and immersive PC games follow in that order,
through the same applications. Each step must leave a usable product.
