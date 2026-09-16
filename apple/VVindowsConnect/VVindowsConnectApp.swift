import SwiftUI
#if canImport(FoveatedStreaming)
import FoveatedStreaming
#endif

@main
struct VVindowsConnectApp: App {
    @State private var connection: Connection
    @State private var desktop: Desktop

    init() {
        let connection = Connection()
        _connection = State(initialValue: connection)
        _desktop = State(initialValue: Desktop(session: connection.session))
    }

    var body: some Scene {
        WindowGroup(id: "home") { HomeView(connection: connection, desktop: desktop) }
            .defaultSize(width: 480, height: 320)
        WindowGroup(id: "desktop") { DesktopView(desktop: desktop) }
            .defaultSize(width: 1600, height: 900)
            .windowResizability(.contentSize)
        #if targetEnvironment(simulator)
        ImmersiveSpace(id: "immersive") { ImmersiveExit(desktop: desktop) }
        #else
        ImmersiveSpace(foveatedStreaming: connection.session) { ImmersiveExit(desktop: desktop) }
            .immersionStyle(selection: .constant(.progressive(0.1...1, initialAmount: 1)), in: .progressive(0.1...1, initialAmount: 1))
        #endif
    }
}
