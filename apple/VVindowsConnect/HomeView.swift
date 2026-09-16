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
            .navigationTitle("vindOS")
            .onAppear {
                connection.homeWindows += 1
                if connection.homeWindows > 1 { return dismissWindow() }
                if desktop.windowOpen, desktop.state == .idle || desktop.immersion != .off { dismissWindow(id: "desktop") }
            }
            .onDisappear { connection.homeWindows -= 1 }
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
                        Button("Forget connection", role: .destructive) {
                            desktop.disconnect()
                            connection.forget()
                            showingSettings = false
                        }
                        .disabled(connection.pair == nil)
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
            Button("Windowed") { desktop.leaveImmersive() }.buttonStyle(.borderedProminent)
        case (_, let pair?):
            Button("Connect") { connect(pair) }
            .buttonStyle(.borderedProminent)
            .disabled(desktop.state == .connecting)
        }
    }
}
