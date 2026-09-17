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
| Windows pre-release | Unsigned 0.1.1 installer compiled with MIT and third-party notices. Installation, reconnect and publication at a new URL remain pending; the website still links 0.1.0 under the earlier pre-release terms. Do not overwrite that old installer. |
| OpenComposite distribution | The bridge matches upstream AppVeyor build 52366409, revision `a27e7e6a64bdcd1eff6b7fba1ea2ea34bcf1273d` (1.0.1539). The new installer includes the corresponding source, pinned submodules, build files and notices in `opencomposite/source.tar.gz`. |
| Other bundled dependencies | The new installer includes QRCoder, OpenXR loader and .NET/Windows Desktop license texts and third-party notices. Published runtime versions are both 10.0.12; publish output contains no NVIDIA runtime files. |
| NVIDIA software | Keep Runtime 6.2.3 and Stream Manager 6.1.0 user-supplied. Confirm a new user can obtain both NGC archives and import them. They are currently required for initial pairing as well as immersive use. |
| Windows signing | Existing v0 decision permits unsigned distribution with a clear publisher warning. Code signing remains a launch-quality improvement, not a claim that the current installer is signed. |
| Vision Pro access | 0.1.0 (2) has processed successfully and is attached to the App Store version. It includes the desktop-window fixes and an in-app privacy-policy link. Price is confirmed free; store copy, requirements, review notes/contact, category, age rating and territory availability are saved. App Privacy is published and App Motion is saved. Three screenshots from the actual headset session are uploaded. Version 0.1.0 (2) was submitted for App Store review on 2026-09-16. On 2026-09-17 Apple asked for a demo video recorded on the headset (Guideline 2.1, Information Needed); the video was linked in the review notes and the version resubmitted the same day. Public release remains manual. |
| Actual app verification | Run the release packages on Windows and Vision Pro: fresh install/import, pair, restart/reconnect, input, immersive/windowed, Steam game handoff/return, Forget, bridge restoration/uninstall. Record versions and hardware. |
| Website | Updated copy states free and open source, links the repository/MIT license, and corrects the setup requirements. Retains pre-release availability until both downloads work. |
| Announcement | Use the draft below after the source is public; invite installation only when the headset link and release package checks are complete. |

## Verification record

The App Store version is submitted and uses manual release. Public release is
authorized once Apple approves and both app packages are ready; no further
confirmation is needed. Keep manual release enabled while the Windows package
checks remain pending.

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
