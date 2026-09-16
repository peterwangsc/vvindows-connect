import SwiftUI
import FoveatedStreaming

@main
struct VVindowsConnectApp: App {
    @State private var connection = Connection()
    @State private var desktop = Desktop()

    var body: some Scene {
        WindowGroup { HomeView(connection: connection, desktop: desktop) }
            .defaultSize(width: 480, height: 320)
        WindowGroup(id: "desktop") { DesktopView(desktop: desktop) }
            .defaultSize(width: 1600, height: 900)
            .windowResizability(.contentSize)
        ImmersiveSpace(foveatedStreaming: connection.session)
            .immersionStyle(selection: .constant(.progressive), in: .progressive)
    }
}
