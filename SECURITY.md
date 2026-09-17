# Security

Report vulnerabilities privately to **peterwangsc@gmail.com**, with vindOS
and the affected version in the subject. Include reproduction steps with
synthetic credentials. Do not publish live QR codes, tokens, keys, pair files,
or unredacted logs in issues or pull requests.

vindOS is a pre-release. Fixes target the current version; there is no promised
support window for older builds.

## Trust boundary

Pairing uses Apple's authenticated QR session. The windowed desktop connection
uses TLS 1.3 and a certificate pin plus a per-pair credential delivered over
that session. Credentials are stored in Apple Keychain and Windows DPAPI for
the current user. **Forget connection** removes the app's saved authorization.

CloudXR's immersive media is not encrypted by the vendor. Use vindOS only on
a trusted local network; do not expose its listeners directly to the internet.
NVIDIA software is downloaded by the user and is covered by NVIDIA's terms.

The Windows log stays on the PC at `%LOCALAPPDATA%\vindOS\log.txt`.
It records session events and diagnostics, including network addresses,
device/game/process names and paths. Review and redact identifying information
before sharing it. The app must never log QR payloads, tokens, or keys.
