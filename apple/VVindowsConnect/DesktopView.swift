import RealityKit
import SwiftUI

struct DesktopView: View {
    let desktop: Desktop
    @State private var keyboardRequest = 0
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
            VideoView(stream: desktop.video, frameSize: frameSize, keyboardRequest: keyboardRequest) { desktop.send($0) }
            switch desktop.state {
            case .connecting: ProgressView("Connecting…")
            case .failed: Text("Could not reach the PC. Check that vindOS is running on it, then Connect again.").padding().glassBackgroundEffect()
            default: EmptyView()
            }
        }
        .frame(minWidth: 480, minHeight: 480 * frameSize.height / frameSize.width)
        .ignoresSafeArea()
        .ornament(attachmentAnchor: .scene(.topLeading), contentAlignment: .topTrailing) {
            DesktopControl("Disconnect", symbol: "chevron.left") {
                desktop.disconnect()
                toHome()
            }
            .padding(.trailing, 16)
            .padding(.top, 12)
        }
        .ornament(attachmentAnchor: .scene(.topTrailing), contentAlignment: .topLeading) {
            if case .streaming = desktop.state {
                DesktopControl("Fullscreen", symbol: "arrow.up.left.and.arrow.down.right") {
                    desktop.enterImmersive(open: openImmersiveSpace, dismiss: dismissImmersiveSpace)
                }
                .disabled(desktop.immersion != .off)
                .padding(.leading, 16)
                .padding(.top, 12)
            }
        }
        .ornament(attachmentAnchor: .scene(.bottomTrailing), contentAlignment: .bottomLeading) {
            if case .streaming = desktop.state {
                DesktopControl("Show Keyboard", symbol: "keyboard") { keyboardRequest += 1 }
                    .disabled(desktop.immersion != .off)
                    .padding(.leading, 16)
                    .padding(.bottom, 12)
            }
        }
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
    }
}

private struct DesktopControl: View {
    let title: String
    let symbol: String
    let action: () -> Void
    @Environment(\.accessibilityVoiceOverEnabled) private var voiceOverEnabled

    init(_ title: String, symbol: String, action: @escaping () -> Void) {
        self.title = title
        self.symbol = symbol
        self.action = action
    }

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.title3.weight(.semibold))
                .frame(width: 52, height: 52)
                .background(.thinMaterial, in: Circle())
                .hoverEffect { effect, active, _ in
                    effect.opacity(active || voiceOverEnabled ? 1 : 0)
                }
        }
        .buttonStyle(.plain)
        .contentShape([.interaction, .hoverEffect], Rectangle())
        .hoverEffect(.highlight)
        .hoverEffectGroup()
        .accessibilityLabel(title)
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
                panel.position = [0, -0.25, -0.5]
                hud = panel
            }
        } update: { content, _ in
            if let shell = content.entities.first(where: { $0.name == "shell" }) {
                if hudShown { shell.components.remove(InputTargetComponent.self) } else { shell.components.set(InputTargetComponent()) }
            }
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
                .frame(width: 20000, height: 12000)
                .contentShape(Rectangle())
                .onTapGesture { hudShown = false }
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
