import SwiftUI

struct DesktopView: View {
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismissWindow) private var dismissWindow

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
        .onAppear {
            desktop.windowOpen = true
            if desktop.state == .idle {
                openWindow(id: "home")
                dismissWindow()
            }
        }
        .onDisappear {
            desktop.windowOpen = false
            desktop.disconnect()
        }
        .ornament(attachmentAnchor: .scene(.top)) {
            Button("Disconnect") {
                desktop.disconnect()
                dismissWindow()
            }
            .padding(8)
            .glassBackgroundEffect()
        }
    }
}
