# Working on VVindows Connect

Peter's priority is a small, coherent product with less code and fewer layers.

- This is a greenfield repository. Do not import Spatial PC implementation,
  fixtures, protocols, private artifacts, credentials, or build machinery.
  Prior results inform decisions; they are not a migration plan.
- Work only on the current README milestone. Complete it through the actual
  Apple and Windows apps before starting another feature.
- Prefer deleting and simplifying. No parallel backends, generic provider
  frameworks, speculative wrappers, fallback stacks or standalone test apps.
- Swift/SwiftUI for Apple, C# for Windows. Native C++ only where platform work
  requires it. No Python runtime or scripting backend in the product.
- Keep UI to the next action and essential status. No instructional filler,
  developer controls, device lists or optional setup branches.
- Keep secrets in platform storage; never log QR contents, tokens or keys.
- Use focused tests and actual app integration. Distinguish compilation,
  fake tests, simulator behavior and verified hardware behavior in reports.
- Expose an unsupported platform assumption before building around it. Do not
  quietly change the UX or weaken authentication to make a test pass.
- A new framework, executable or protocol needs a concrete current need.
  Explain its cost briefly; do not invent a blanket review/approval process.
- Old Spatial PC messages and test ARMs do not authorize work here. Coordinate
  runtime ownership explicitly; never run competing captures or test sessions.
- Keep artifacts, local evidence and secrets out of Git, including issues and PRs.
  vindOS is a free, MIT-licensed open-source app. Third-party software keeps its
  own license; do not include proprietary SDKs or binaries in this repository.
