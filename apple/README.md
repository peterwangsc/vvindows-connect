# Apple app

Swift/SwiftUI app for Apple Vision Pro, with windowed desktop and immersive
Foveated Streaming sessions.

## Build

Use a Mac with Xcode and the visionOS 26.4 SDK or later. Open
`VVindowsConnect.xcodeproj` and select the shared `VVindowsConnect` scheme.
Select your signing team for a physical headset build; the checked-in team
and bundle identifier identify the official app, not credentials you can use.
Device provisioning must support the
`com.apple.developer.foveated-streaming-session` entitlement.

Compile the actual app for the simulator without signing:

```sh
xcodebuild -project apple/VVindowsConnect.xcodeproj \
  -scheme VVindowsConnect -configuration Debug \
  -destination 'generic/platform=visionOS Simulator' \
  CODE_SIGNING_ALLOWED=NO build
```

Run that command from the repository root. A simulator build does not verify
Apple's QR pairing, Foveated Streaming, permissions, or headset input. Those
require a Vision Pro and the Windows app on the same network. `Simulator.swift`
only supports previewing the actual app's UI.

## App flow

Pairing completes back at **Connect**. Connect opens the desktop window;
fullscreen requests Immersive Mode. Settings holds **Forget connection**.
Saved credentials use Keychain. Bonjour advertises/discovers the application
identifier used by both apps; if you change it for a fork, update the matching
Windows advertisement too.

For a release, archive the actual app for visionOS and distribute through
your Apple developer account. The current public-distribution status and
remaining hardware verification are in [LAUNCH.md](../LAUNCH.md).