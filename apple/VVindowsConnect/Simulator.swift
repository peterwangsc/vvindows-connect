#if targetEnvironment(simulator)
import Foundation
import Network
import Observation
import SwiftUI

@MainActor @Observable
final class FoveatedStreamingSession {
    struct Endpoint {
        static let systemDiscovered = Endpoint()
        static func local(ipAddress: any IPAddress, port: NWEndpoint.Port) -> Endpoint { Endpoint() }
    }
    struct ImmersivePresentationBehaviors: ExpressibleByArrayLiteral {
        init(arrayLiteral: ImmersivePresentationBehaviors...) {}
        init() {}
        static func presentOnConnect(_ open: OpenImmersiveSpaceAction) -> Self { .init() }
        static func dismissOnDisconnect(_ dismiss: DismissImmersiveSpaceAction) -> Self { .init() }
    }
    var immersivePresentationBehaviors = ImmersivePresentationBehaviors()
    struct DisconnectReason: Error, Equatable { static let unavailable = DisconnectReason() }
    enum Status: Equatable { case initialized, connecting, connected, disconnected(DisconnectReason) }

    @MainActor final class MessageChannel {
        struct ID: Hashable { let uuid = UUID() }
        let id = ID()
        let receivedMessageStream: AsyncStream<Data>
        init(_ data: Data) { receivedMessageStream = AsyncStream { $0.yield(data); $0.finish() } }
    }

    private(set) var status = Status.initialized
    private(set) var availableMessageChannels = Set<MessageChannel.ID>()
    private var channel: MessageChannel?

    func connect(endpoint: Endpoint) async throws {
        status = .connecting
        try await Task.sleep(for: .seconds(1))
        guard let json = ProcessInfo.processInfo.environment["VINDOS_SIM_PAIRED"] else {
            status = .disconnected(.unavailable)
            throw DisconnectReason.unavailable
        }
        status = .connected
        channel = MessageChannel(Data(json.utf8))
        availableMessageChannels = [channel!.id]
    }

    func messageChannel(for id: MessageChannel.ID) -> MessageChannel? { channel?.id == id ? channel : nil }

    func disconnect() async {
        status = .disconnected(.unavailable)
        availableMessageChannels = []
        channel = nil
    }
}
#endif

#if targetEnvironment(simulator)
@MainActor
enum SimulatorScript {
    private static var ran = false

    static func run(_ connection: Connection, _ desktop: Desktop, connect: @escaping () -> Void, disconnect: @escaping () -> Void) {
        guard !ran, let script = ProcessInfo.processInfo.environment["VINDOS_SIM_ACTIONS"] else { return }
        ran = true
        Task {
            for step in script.split(separator: ",") {
                switch step {
                case "pair": connection.startPairing()
                case "forget": connection.forget()
                case "connect": connect()
                case "disconnect": disconnect()
                default: try? await Task.sleep(for: .seconds(Double(step) ?? 1))
                }
            }
        }
    }
}
#endif
