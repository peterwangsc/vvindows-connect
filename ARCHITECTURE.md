# Architecture

## Fewer parts

Two native applications. One implementation of each behavior. Shared material
is a small wire specification when needed, not a cross-platform application
framework. Dependencies are introduced only for the current working feature.

| Boundary | Owner |
| --- | --- |
| Apple UI, system pairing, session and display | One SwiftUI app |
| Windows UI, pairing, credential storage and session | One C# desktop app |
| Apple secrets | Keychain |
| Windows secrets | Current-user DPAPI |
| Windows graphics/input | Native platform APIs; a small C++ module only where required |
| Immersive transport | Supported Apple/NVIDIA APIs, with their mandatory permissions |

The Windows app is the host. No Python backend, local web server, service
installer, orchestration framework, or chain of launcher/owner/bridge apps.
A vendor-required process is owned directly by the app that starts it. Add
another process only for a demonstrated platform or crash-isolation need.

UI calls concrete application operations. Introduce an abstraction when two
real uses need it, not in anticipation of hypothetical backends.

## Pairing and lifetime

Clicking Windows **Pair** opens one bounded enrollment attempt and provides
local consent. The supported Apple QR flow authenticates the system session;
only that authenticated session may establish the app's saved connection.
Discovery, a ClientID, a channel UUID, or a QR-display callback is not proof
of identity. Use platform cryptography and preserve vendor protocol fields.

There is one saved pair, held separately from the current session. Uncertain
delivery must resume that pair rather than silently mint a replacement.
Ordinary restart keeps trust. Forget removes app authorization and ends the
owned session before a replacement is admitted. Credentials must have an
explicit application-owned lifecycle; vendor shared state is not a reset API.

Pairing may require Apple's initial Allow prompt. It must not automatically
present an immersive space, start a game, or connect the desktop. Afterward,
Connect and fullscreen are separate user actions. Reuse of Apple's QR trust
must be demonstrated on hardware before promising scan-once behavior.

One session owner handles connect, cancel, disconnect and shutdown. Resources
are acquired there and released there. Errors return to a recoverable screen;
there is no fallback to a second implementation.

## Build complete slices

1. **Pair and reconnect:** actual Windows QR, actual Apple flow, saved trust,
   restart, cancellation and Forget. Resolve the supported credential/session
   path here before adding desktop or game plumbing.
2. **Windowed desktop:** authenticated connection, one video path, display,
   disconnect and reconnect. Measure before optimizing.
3. **Input:** pointer, physical and virtual keyboard, balanced releases on
   disconnect. Verify in the real windowed product.
4. **Immersive:** the same saved connection, explicit fullscreen/Allow, one
   owned PC application, and reliable return to the desktop.

Choose the concrete SDK and transport at the slice that needs them. Do not
prebuild multiple codecs, transports, pairing schemes or compatibility layers.
If a supported API cannot provide the desired behavior, state that constraint
before changing the user experience or adding a workaround.

Tests live beside the production operation they exercise. Unit tests protect
important invariants; simulator tests run the actual app path where supported.
Physical checks cover Apple system QR, permissions and input. Separate proof
programs do not count as product integration. Diagnostics stay bounded and
credential-free inside the apps; do not create a parallel diagnostic product.

When an approach is replaced, remove it and its dependencies in the same
change. Git retains history. Source size, dependency count and process count
are design costs, not measures of progress.
