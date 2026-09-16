import SwiftUI

struct DesktopView: View {
    let desktop: Desktop

    var body: some View {
        ZStack {
            VideoView(stream: desktop.video)
            switch desktop.state {
            case .connecting: ProgressView("Connecting…")
            case .failed(let reason): Text(reason).padding().glassBackgroundEffect()
            default: EmptyView()
            }
        }
        .aspectRatio(16 / 9, contentMode: .fit)
        .ornament(attachmentAnchor: .scene(.bottom)) {
            Button("Disconnect", systemImage: "xmark") { desktop.disconnect() }
                .labelStyle(.iconOnly)
                .padding(8)
                .glassBackgroundEffect()
        }
    }
}
