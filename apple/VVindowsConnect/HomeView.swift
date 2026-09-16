import SwiftUI

struct HomeView: View {
    let connection: Connection
    let desktop: Desktop
    @Environment(\.openWindow) private var openWindow
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
            .toolbar {
                Button("Settings", systemImage: "gear") { showingSettings = true }
            }
            .sheet(isPresented: $showingSettings) {
                List {
                    Button("Forget connection", role: .destructive) {
                        desktop.disconnect()
                        connection.forget()
                        showingSettings = false
                    }
                    .disabled(connection.pair == nil)
                }
                .presentationDetents([.medium])
            }
        }
    }

    private var status: String {
        switch (connection.activity, connection.pair) {
        case (.pairing, _): "Pairing…"
        case (_, let pair?): "Paired with \(pair.hostName)"
        default: "Not paired"
        }
    }

    @ViewBuilder private var action: some View {
        switch (connection.activity, connection.pair) {
        case (.pairing, _): Button("Cancel") { connection.cancelPairing() }
        case (_, nil): Button("Pair") { connection.startPairing() }.buttonStyle(.borderedProminent)
        case (_, let pair?):
            Button("Connect") {
                desktop.connect(to: pair)
                openWindow(id: "desktop")
            }
            .buttonStyle(.borderedProminent)
            .disabled(desktop.state != .idle)
        }
    }
}
