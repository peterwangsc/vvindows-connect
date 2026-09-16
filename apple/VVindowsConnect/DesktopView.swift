import RealityKit
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
            desktop.desktopWindows += 1
            if desktop.state == .idle { return openWindow(id: "home") }
            dismissWindow(id: "home")
        }
        .onDisappear {
            desktop.desktopWindows -= 1
            Task {
                try? await Task.sleep(for: .seconds(1))
                if desktop.desktopWindows == 0, desktop.immersion == .off, !desktop.reopening { desktop.disconnect() }
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
            }
            .padding(8)
            .glassBackgroundEffect()
        }
    }
}

struct ImmersiveContent: View {
    let connection: Connection
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow
    @State private var hudShown = false
    @State private var hud: Entity?
    @State private var mount: Entity?

    var body: some View {
        RealityView { content, attachments in
            var material = UnlitMaterial(color: .clear)
            material.blending = .transparent(opacity: .init(floatLiteral: 0))
            let shell = ModelEntity(mesh: .generateSphere(radius: 30), materials: [material])
            shell.name = "shell"
            shell.components.set(InputTargetComponent())
            shell.components.set(CollisionComponent(shapes: [.generateSphere(radius: 30)]))
            content.add(shell)
            if let panel = attachments.entity(for: "hud") {
                panel.isEnabled = false
                panel.position = [0, -0.2, -0.4]
                hud = panel
            }
        } update: { content, _ in
            if hudShown, let hud, mount == nil {
                let anchor = AnchorEntity(.head, trackingMode: .once)
                anchor.addChild(hud)
                content.add(anchor)
                hud.isEnabled = true
                Task { @MainActor in mount = anchor }
            } else if !hudShown, let mount {
                hud?.isEnabled = false
                content.remove(mount)
                Task { @MainActor in self.mount = nil }
            }
        } attachments: {
            Attachment(id: "hud") {
                VStack(spacing: 12) {
                    Text(status).font(.headline)
                    HStack(spacing: 12) {
                        Button("Recenter") { desktop.recenter() }.buttonStyle(.borderedProminent)
                        Button("Windowed") { desktop.leaveImmersive() }
                    }
                }
                .controlSize(.large)
                .padding(20)
                .glassBackgroundEffect()
            }
        }
        .gesture(SpatialTapGesture().targetedToAnyEntity().onEnded { value in if value.entity.name == "shell" { hudShown.toggle() } })
        .onAppear {
            #if targetEnvironment(simulator)
            SimulatorScript.actions["door"] = { hudShown.toggle() }
            #endif
        }
        .onDisappear {
            desktop.leaveImmersive()
            desktop.reopening = true
            openWindow(id: "desktop")
        }
    }

    private var status: String {
        switch desktop.game {
        case .running(let name): "Playing \(name)"
        case .stopped(let name): "\(name) stopped"
        case .none: "Fullscreen on \(connection.pair?.hostName ?? "PC")"
        }
    }
}
