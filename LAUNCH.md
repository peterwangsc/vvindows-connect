# Free, open-source v0 launch

The official vindOS app is free: no subscription and no paid feature unlocks.
The source is MIT-licensed. Hardware, games, and third-party services/software
are obtained separately under their own terms.

## Readiness

Opening the source and launching downloadable apps are separate deliverables.
Do not describe the public app launch as complete until a new user can install
both apps and finish the actual flow.

| Item | State / next action |
| --- | --- |
| Source license | MIT, copyright Peter Wang. Root license is also the Windows installer license page. |
| GitHub visibility | Public MIT source at https://github.com/peterwangsc/vvindows-connect. Git history, PR heads, and repository discussions were scanned before publication; the single history finding was a verified binary checksum, not a credential. |
| Contributor entry point | README, platform build instructions, contributing guide, and private security contact. |
| Windows pre-release | Existing unsigned 0.1.0 installer is on the website under the earlier pre-release terms. Rebuild and version the public package with MIT and third-party notices; do not overwrite the old installer at the same URL. |
| OpenComposite distribution | Identify the exact source for the shipped DLL, include corresponding source and dependency notices, or build a documented revision and reverify game handoff. The current checkout has only the DLL. |
| Other bundled dependencies | Include QRCoder, OpenXR loader, and self-contained .NET/Windows Desktop license texts and third-party notices in the release package. |
| NVIDIA software | Keep Runtime 6.2.3 and Stream Manager 6.1.0 user-supplied. Confirm a new user can obtain both NGC archives and import them. They are currently required for initial pairing as well as immersive use. |
| Windows signing | Existing v0 decision permits unsigned distribution with a clear publisher warning. Code signing remains a launch-quality improvement, not a claim that the current installer is signed. |
| Vision Pro access | 0.1.0 (1) was uploaded to TestFlight. Website still has no public invitation. Verify current App Store Connect review state and make an external TestFlight link available. Set App Store price to free before an App Store release. |
| Actual app verification | Run the release packages on Windows and Vision Pro: fresh install/import, pair, restart/reconnect, input, immersive/windowed, Steam game handoff/return, Forget, bridge restoration/uninstall. Record versions and hardware. |
| Website | Updated copy states free and open source, links the repository/MIT license, and corrects the setup requirements. Retains pre-release availability until both downloads work. |
| Announcement | Use the draft below after the source is public; invite installation only when the headset link and release package checks are complete. |

## Verification record

Hardware observations from 2026-09-16 are summarized in the README. They are
historical evidence, not a fresh verification of the final release artifacts.
Latest Apple desktop-window changes must be included in the release run.
No new capture or headset session is implied by build or documentation checks.
Keep detailed logs, scan reports, hashes, and build evidence under ignored
`.local/`, with release checksums attached to the release rather than committed.

## Announcement draft

**vindOS: your Windows PC on Apple Vision Pro, free and open source.**

I'm building vindOS for people who want their PC on Vision Pro without another
app subscription. It brings your Windows desktop into a window, gives you a
large immersive screen, and supports Steam VR game handoff. The code is MIT
licensed, and the official app is free with no paid unlocks.

This is an early release. Testing so far covers an RTX 4070 and Assetto Corsa;
it is not a promise that every GPU or game will work. You need visionOS 26.4+,
a Windows 11 x64 PC with a compatible NVIDIA GPU, and NVIDIA's separately
downloaded CloudXR Runtime and Stream Manager. The Windows pre-release is
unsigned, and public Vision Pro access is still being prepared.

Source: https://github.com/peterwangsc/vvindows-connect

Availability and setup: https://www.peterwang.tech/vindos
