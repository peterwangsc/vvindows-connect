# Windows app

C# native desktop application. The app owns the host session directly.

The home screen contains connection status, **Pair**, and Settings. Pair
shows the QR in the same window and authorizes one authenticated enrollment.
No PIN, second approval, device list or Windows-side Connect button.

Build the matching pairing slice here. No application source has been added
yet. Use supported vendor APIs directly where practical; do not bring over
the previous host, helper hierarchy or installer. Runtime dependencies must
eventually be included in normal installation rather than surprise the user.
