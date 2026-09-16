import SwiftUI
import FoveatedStreaming

@main
struct VVindowsConnectApp: App {
    @State private var connection = Connection()

    var body: some Scene {
        WindowGroup { HomeView(connection: connection) }
            .defaultSize(width: 480, height: 320)
        ImmersiveSpace(foveatedStreaming: connection.session)
            .immersionStyle(selection: .constant(.progressive), in: .progressive)
    }
}
