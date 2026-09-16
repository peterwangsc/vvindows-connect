#if targetEnvironment(simulator)
import Foundation
import Network
import Observation
import SwiftUI

@MainActor @Observable
final class FoveatedStreamingSession {
    struct Endpoint {
        var local = false
        static let systemDiscovered = Endpoint()
        static func local(ipAddress: any IPAddress, port: NWEndpoint.Port) -> Endpoint { Endpoint(local: true) }
    }
    struct ImmersivePresentationBehaviors: ExpressibleByArrayLiteral {
        var open: OpenImmersiveSpaceAction?
        var dismiss: DismissImmersiveSpaceAction?
        init(arrayLiteral: ImmersivePresentationBehaviors...) { arrayLiteral.forEach { open = open ?? $0.open; dismiss = dismiss ?? $0.dismiss } }
        init() {}
        static func presentOnConnect(_ open: OpenImmersiveSpaceAction) -> Self { var b = Self(); b.open = open; return b }
        static func dismissOnDisconnect(_ dismiss: DismissImmersiveSpaceAction) -> Self { var b = Self(); b.dismiss = dismiss; return b }
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
        if endpoint.local {
            status = .connected
            await immersivePresentationBehaviors.open?(id: "immersive")
            return
        }
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
        let wasConnected = status == .connected
        status = .disconnected(.unavailable)
        availableMessageChannels = []
        channel = nil
        if wasConnected { await immersivePresentationBehaviors.dismiss?() }
    }
}
#endif

#if targetEnvironment(simulator)
@MainActor
enum SimulatorScript {
    private static var ran = false

    static var actions: [String: () -> Void] = [:]

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
                case "windowed": desktop.leaveImmersive()
                default: if let action = actions[String(step)] { action() } else { try? await Task.sleep(for: .seconds(Double(step) ?? 1)) }
                }
            }
        }
    }
}
#endif
