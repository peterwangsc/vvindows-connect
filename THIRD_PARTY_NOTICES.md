# Third-party software

The [MIT license](LICENSE) covers vindOS's own source and assets. It does not
relicense the software listed below. Vendor binaries are not in this Git
repository. Release packages must carry the applicable upstream license texts
and notices, including those for bundled runtime dependencies.

| Component | Use | License / source |
| --- | --- | --- |
| QRCoder 1.6.0 | Windows QR generation | MIT; https://github.com/codebude/QRCoder/tree/v1.6.0 |
| OpenXR.Loader 1.0.6.2 | Native Windows OpenXR loader | Apache-2.0; https://www.nuget.org/packages/OpenXR.Loader/1.0.6.2 |
| .NET / Windows Desktop runtime | Self-contained Windows distribution | MIT and upstream third-party notices; https://github.com/dotnet/runtime and https://github.com/dotnet/wpf |
| OpenComposite, OpenXR branch | Bridge copied into user-selected OpenVR games | GPL-3.0-or-later and bundled dependencies; https://gitlab.com/znixian/OpenOVR/-/tree/openxr |
| NVIDIA CloudXR Runtime 6.2.3 and Stream Manager 6.1.0 | Pairing and immersive transport | NVIDIA proprietary terms, accepted separately through https://catalog.ngc.nvidia.com/ |

## Binary distribution

OpenComposite is a separate bridge loaded by games; vindOS does not relicense
it under MIT. When distributing its binary, supply the exact corresponding
source (including its build files and required submodule sources) and all
applicable notices alongside it. A moving branch link alone is not a record
of the source used to build a shipped DLL.

The existing local bridge binary has no accompanying source provenance in this
repository. Resolving that is a release task in [LAUNCH.md](LAUNCH.md), not a
reason to silently remove game support or claim the binary is MIT-licensed.

The Windows installer excludes NVIDIA runtime files. Users download and
accept NVIDIA's terms themselves; vindOS imports their archives locally.
The project license does not grant any NVIDIA redistribution rights.

Apple, Microsoft, NVIDIA, Valve, Steam, and game names are trademarks of their
respective owners. vindOS is not affiliated with or endorsed by those vendors.
