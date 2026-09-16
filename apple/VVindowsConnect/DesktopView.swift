import SwiftUI

struct DesktopView: View {
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismissWindow) private var dismissWindow
    @Environment(\.openImmersiveSpace) private var openImmersiveSpace
    @Environment(\.dismissImmersiveSpace) private var dismissImmersiveSpace

    private var frameSize: CGSize {
        if case .streaming(let w, let h) = desktop.state, w > 0, h > 0 { return CGSize(width: w, height: h) }
        return CGSize(width: 16, height: 9)
    }

    private func toHome() {
        openWindow(id: "home")
        dismissWindow(id: "desktop")
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
            #if targetEnvironment(simulator)
            SimulatorScript.actions["fullscreen"] = { desktop.enterImmersive(open: openImmersiveSpace, dismiss: dismissImmersiveSpace) }
            SimulatorScript.actions["crown"] = { Task { await dismissImmersiveSpace() } }
            SimulatorScript.actions["closedesktop"] = { dismissWindow(id: "desktop") }
            #endif
            desktop.reopening = false
            if desktop.state == .idle { return toHome() }
            desktop.windowOpen = true
            dismissWindow(id: "home")
        }
        .onDisappear {
            desktop.windowOpen = false
            Task {
                try? await Task.sleep(for: .seconds(1))
                if !desktop.windowOpen, desktop.immersion == .off, !desktop.reopening { desktop.disconnect() }
            }
        }
        .onChange(of: desktop.immersion) { _, immersion in
            if immersion == .on { toHome() }
        }
        .onChange(of: desktop.session.status) { _, status in
            switch status {
            case .connected:
                desktop.immersiveConnected()
                toHome()
            case .disconnected: if desktop.immersion == .on { desktop.leaveImmersive() }
            default: break
            }
        }
        .onChange(of: desktop.state) { _, state in
            if state == .idle { toHome() }
        }
        .ornament(attachmentAnchor: .scene(.top)) {
            HStack(spacing: 16) {
                Button("Disconnect") {
                    desktop.disconnect()
                    toHome()
                }
                if case .streaming = desktop.state {
                    switch desktop.immersion {
                    case .off: Button("Fullscreen") { desktop.enterImmersive(open: openImmersiveSpace, dismiss: dismissImmersiveSpace) }
                    default: ProgressView()
                    }
                }
                Text(desktop.sent.sorted { $0.key < $1.key }.map { "\($0.key):\($0.value)" }.joined(separator: " "))
                    .font(.caption.monospaced())
            }
            .padding(8)
            .glassBackgroundEffect()
        }
    }
}

struct ImmersiveExit: View {
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Color.clear.onDisappear {
            desktop.leaveImmersive()
            desktop.reopening = true
            openWindow(id: "desktop")
        }
    }
}
