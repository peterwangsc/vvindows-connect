import SwiftUI

struct HomeView: View {
    let connection: Connection
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismissWindow) private var dismissWindow
    @State private var showingSettings = false

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Text(status).font(.title2)
                if case .failed(let reason) = connection.activity {
                    Text(reason).foregroundStyle(.secondary)
                }
                action
            }
            .padding(32)
            .controlSize(.extraLarge)
            .navigationTitle("vindOS")
            .onAppear {
                #if targetEnvironment(simulator)
                SimulatorScript.actions["closehome"] = { dismissWindow(id: "home") }
                #endif
                connection.homeWindows += 1
                if connection.homeWindows > 1 { return dismissWindow() }
                if desktop.desktopWindows > 0, desktop.state == .idle || desktop.immersion != .off { dismissWindow(id: "desktop") }
            }
            .onDisappear { connection.homeWindows -= 1 }
            .onChange(of: desktop.session.status) { _, status in
                guard status == .connected, desktop.immersion == .on else { return }
                Task {
                    try? await Task.sleep(for: .seconds(1))
                    if desktop.immersion == .on { dismissWindow(id: "home") }
                }
            }
            .onChange(of: desktop.immersion) { _, immersion in
                if immersion == .on { Task { try? await Task.sleep(for: .seconds(1)); if desktop.immersion == .on { dismissWindow(id: "home") } } }
            }
            .task {
                #if targetEnvironment(simulator)
                SimulatorScript.run(connection, desktop, connect: { connection.pair.map(connect) }, disconnect: desktop.disconnect)
                #endif
            }
            .toolbar {
                Button("Settings", systemImage: "gear") { showingSettings = true }
            }
            .sheet(isPresented: $showingSettings) {
                NavigationStack {
                    List {
                        if let pair = connection.pair {
                            LabeledContent("PC", value: pair.hostName)
                            LabeledContent("Paired", value: pair.pairedAt.formatted(date: .abbreviated, time: .shortened))
                        }
                        Button("Forget connection", role: .destructive) {
                            desktop.disconnect()
                            connection.forget()
                            showingSettings = false
                        }
                        .disabled(connection.pair == nil)
                        LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")
                    }
                    .navigationTitle("Settings")
                    .toolbar { Button("Done") { showingSettings = false } }
                }
            }
        }
    }

    private func connect(_ pair: SavedPair) {
        desktop.connect(to: pair)
        openWindow(id: "desktop")
    }

    private var status: String {
        switch (connection.activity, connection.pair) {
        case (.pairing, _): "Pairing…"
        case (_, let pair?) where desktop.immersion != .off: "Fullscreen on \(pair.hostName)"
        case (_, let pair?): "Paired with \(pair.hostName)"
        default: "Not paired"
        }
    }

    @ViewBuilder private var action: some View {
        switch (connection.activity, connection.pair) {
        case (.pairing, _): Button("Cancel") { connection.cancelPairing() }
        case (_, nil): Button("Pair") { connection.startPairing() }.buttonStyle(.borderedProminent)
        case (_, _?) where desktop.immersion != .off:
            Button("Windowed") { desktop.leaveImmersive() }
        case (_, let pair?):
            Button("Connect") { connect(pair) }
            .buttonStyle(.borderedProminent)
            .disabled(desktop.state == .connecting)
        }
    }
}
