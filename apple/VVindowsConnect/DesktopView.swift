import SwiftUI

struct DesktopView: View {
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismissWindow) private var dismissWindow

    private var frameSize: CGSize {
        if case .streaming(let w, let h) = desktop.state, w > 0, h > 0 { return CGSize(width: w, height: h) }
        return CGSize(width: 16, height: 9)
    }

    var body: some View {
        ZStack {
            VideoView(stream: desktop.video, frameSize: frameSize) { desktop.send($0) }
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
        .onChange(of: desktop.state) { _, state in
            guard state == .idle else { return }
            openWindow(id: "home")
            dismissWindow()
        }
        .ornament(attachmentAnchor: .scene(.top)) {
            HStack(spacing: 16) {
                Button("Disconnect") { desktop.disconnect() }
                Text(desktop.sent.sorted { $0.key < $1.key }.map { "\($0.key):\($0.value)" }.joined(separator: " "))
                    .font(.caption.monospaced())
            }
            .padding(8)
            .glassBackgroundEffect()
        }
    }
}
