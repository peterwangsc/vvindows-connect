import Foundation
#if canImport(FoveatedStreaming)
import FoveatedStreaming
#endif
import Observation

@MainActor @Observable
final class Connection {
    enum Activity: Equatable { case idle, pairing, failed(String) }

    let session = FoveatedStreamingSession()
    private(set) var pair = SavedPair.load()
    private(set) var activity = Activity.idle
    var homeWindows = 0
    private var pairing: Task<Void, Never>?

    func startPairing() {
        activity = .pairing
        pairing = Task {
            do {
                try await session.connect(endpoint: .systemDiscovered)
                let paired = try await withThrowingTaskGroup(of: PairedMessage.self) { group in
                    group.addTask { try await self.firstMessage() }
                    group.addTask { try await Task.sleep(for: .seconds(20)); throw PairingError.noHostMessage }
                    defer { group.cancelAll() }
                    return try await group.next()!
                }
                pair = try SavedPair(paired).saved()
                activity = .idle
            } catch is CancellationError {
                activity = .idle
            } catch {
                activity = .failed(error.localizedDescription)
            }
            await session.disconnect()
        }
    }

    func cancelPairing() { pairing?.cancel() }

    func forget() {
        SavedPair.delete()
        pair = nil
    }

    private func firstMessage() async throws -> PairedMessage {
        while true {
            if case .disconnected(let reason) = session.status { throw reason }
            if let id = session.availableMessageChannels.first, let channel = session.messageChannel(for: id) {
                for await data in channel.receivedMessageStream { return try PairedMessage(data) }
                throw PairingError.channelClosed
            }
            await withCheckedContinuation { (next: CheckedContinuation<Void, Never>) in
                withObservationTracking { _ = (session.availableMessageChannels, session.status) } onChange: { next.resume() }
            }
        }
    }
}

enum PairingError: Error, LocalizedError {
    case noHostMessage, channelClosed, unexpectedMessage
    var errorDescription: String? {
        switch self {
        case .noHostMessage: "The PC did not finish pairing."
        case .channelClosed: "The PC closed the connection."
        case .unexpectedMessage: "The PC sent an unexpected message."
        }
    }
}
