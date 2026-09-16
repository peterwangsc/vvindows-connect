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
    @State private var head: Entity?
    @State private var hud: Entity?

    var body: some View {
        RealityView { content, attachments in
            var material = UnlitMaterial(color: .clear)
            material.blending = .transparent(opacity: .init(floatLiteral: 0))
            let shell = ModelEntity(mesh: .generateSphere(radius: 3), materials: [material])
            shell.name = "shell"
            shell.components.set(CollisionComponent(shapes: [.generateSphere(radius: 3)]))
            content.add(shell)
            let headAnchor = AnchorEntity(.head)
            content.add(headAnchor)
            if let panel = attachments.entity(for: "hud") {
                panel.isEnabled = false
                content.add(panel)
                hud = panel
            }
            head = headAnchor
        } update: { content, _ in
            if let shell = content.entities.first(where: { $0.name == "shell" }) {
                if hudShown { shell.components.remove(InputTargetComponent.self) } else { shell.components.set(InputTargetComponent()) }
            }
            hud?.isEnabled = hudShown
        } attachments: {
            Attachment(id: "hud") {
                VStack(spacing: 12) {
                    Text(status).font(.headline)
                    HStack(spacing: 12) {
                        Button("Recenter") { desktop.recenter() }.buttonStyle(.borderedProminent)
                        Button("Windowed") { desktop.leaveImmersive() }
                        Button("Hide") { hudShown = false }
                    }
                }
                .controlSize(.large)
                .padding(20)
                .glassBackgroundEffect()
            }
        }
        .gesture(SpatialTapGesture().targetedToAnyEntity().onEnded { _ in show() })
        .onAppear {
            #if targetEnvironment(simulator)
            SimulatorScript.actions["door"] = { if hudShown { hudShown = false } else { show() } }
            #endif
        }
        .onDisappear {
            desktop.leaveImmersive()
            desktop.reopening = true
            openWindow(id: "desktop")
        }
    }

    private func show() {
        if let head, let hud {
            let eye = head.position(relativeTo: nil)
            var forward = head.orientation(relativeTo: nil).act([0, 0, -1])
            forward.y = 0
            if simd_length(forward) < 0.01 { forward = [0, 0, -1] }
            forward = simd_normalize(forward)
            let spot = eye + forward * 0.9 + SIMD3<Float>(0, -0.45, 0)
            hud.look(at: SIMD3<Float>(eye.x, spot.y, eye.z), from: spot, relativeTo: nil)
            hud.orientation *= simd_quatf(angle: .pi, axis: [0, 1, 0])
        }
        hudShown = true
    }

    private var status: String {
        switch desktop.game {
        case .running(let name): "Playing \(name)"
        case .stopped(let name): "\(name) stopped"
        case .none: "Fullscreen on \(connection.pair?.hostName ?? "PC")"
        }
    }
}
