import CryptoKit
import Foundation
import Network
import Observation
import SwiftUI
#if canImport(FoveatedStreaming)
import FoveatedStreaming
#endif

@MainActor @Observable
final class Desktop {
    enum State: Equatable { case idle, connecting, streaming(width: Int, height: Int), failed(String) }
    enum Immersion: Equatable { case off, starting, on }
    struct Game: Decodable, Equatable, Identifiable { let id: String; let name: String }
    enum GameState: Equatable { case none, starting(Game), running(Game), failed(String) }

    let session: FoveatedStreamingSession
    private(set) var state = State.idle
    private(set) var immersion = Immersion.off
    private(set) var games: [Game] = []
    private(set) var latency = ""
    private var clock: [(rtt: Int64, offset: Int64)] = []
    private var samples: [Int64] = []
    private var dropped = 0
    private var ticker: Task<Void, Never>?
    private(set) var game = GameState.none
    private var hostAddress: (any IPAddress)?
    var desktopWindows = 0
    var reopening = false
    private(set) var sent = [UInt8: Int]()
    let video = VideoStream()
    private var connection: NWConnection?
    private var browser: NWBrowser?

    init(session: FoveatedStreamingSession) { self.session = session }

    func enterImmersive(open: OpenImmersiveSpaceAction, dismiss: DismissImmersiveSpaceAction) {
        guard case .streaming = state, immersion == .off else { return }
        immersion = .starting
        session.immersivePresentationBehaviors = [.presentOnConnect(open), .dismissOnDisconnect(dismiss)]
        connection?.send(content: Frame.control(["v": 1, "type": "immersive"]), completion: .idempotent)
    }

    func immersiveConnected() {
        if immersion == .starting { immersion = .on }
    }

    func play(_ game: Game) {
        guard immersion == .on else { return }
        self.game = .starting(game)
        connection?.send(content: Frame.control(["v": 1, "type": "game", "id": game.id]), completion: .idempotent)
    }

    func recenter() {
        guard case .running = game else { return }
        connection?.send(content: Frame.control(["v": 1, "type": "recenter"]), completion: .idempotent)
    }

    func leaveImmersive() {
        guard immersion != .off else { return }
        immersion = .off
        game = .none
        connection?.send(content: Frame.control(["v": 1, "type": "windowed"]), completion: .idempotent)
        Task { await session.disconnect() }
    }

    func connect(to pair: SavedPair) {
        disconnect()
        state = .connecting
        let browser = NWBrowser(for: .bonjourWithTXTRecord(type: "_apple-foveated-streaming._tcp", domain: nil), using: .tcp)
        self.browser = browser
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            guard let match = results.first(where: { result in
                if case .bonjour(let txt) = result.metadata { return txt["ServerID"] == pair.serverId }
                return false
            }), case .bonjour(let txt) = match.metadata, let port = txt["DesktopPort"].flatMap(UInt16.init),
                  case .service(let name, _, _, _) = match.endpoint else { return }
            Task { @MainActor in
                guard let self, self.browser === browser else { return }
                browser.cancel()
                self.browser = nil
                self.open(.hostPort(host: .init("\(name).local"), port: .init(rawValue: port)!), pair: pair)
            }
        }
        browser.start(queue: .main)
    }

    func send(_ input: Input) {
        guard case .streaming = state else { return }
        sent[input.record[0], default: 0] += 1
        connection?.send(content: Frame.input(input.record), completion: .idempotent)
    }

    func disconnect() {
        ticker?.cancel()
        clock.removeAll()
        latency = ""
        if immersion != .off { leaveImmersive() }
        browser?.cancel()
        browser = nil
        if let connection {
            connection.send(content: Frame.control(["v": 1, "type": "bye"]), completion: .contentProcessed { _ in connection.cancel() })
        }
        connection = nil
        video.reset()
        state = .idle
    }

    private func open(_ endpoint: NWEndpoint, pair: SavedPair) {
        let tls = NWProtocolTLS.Options()
        let options = tls.securityProtocolOptions
        sec_protocol_options_set_min_tls_protocol_version(options, .TLSv13)
        sec_protocol_options_add_tls_application_protocol(options, "vindos/1")
        sec_protocol_options_set_verify_block(options, { _, trust, complete in
            let chain = SecTrustCopyCertificateChain(sec_trust_copy_ref(trust).takeRetainedValue()) as? [SecCertificate]
            let leaf = chain?.first.map { SHA256.hash(data: SecCertificateCopyData($0) as Data).map { String(format: "%02x", $0) }.joined() }
            complete(leaf == pair.desktop.sha256)
        }, .main)
        let connection = NWConnection(to: endpoint, using: NWParameters(tls: tls, tcp: .init()))
        self.connection = connection
        connection.stateUpdateHandler = { [weak self] change in
            MainActor.assumeIsolated {
                guard let self, self.connection === connection else { return }
                switch change {
                case .ready:
                    if case .hostPort(let host, _)? = connection.currentPath?.remoteEndpoint {
                        switch host {
                        case .ipv4(let a): self.hostAddress = a
                        case .ipv6(let a): self.hostAddress = a
                        default: break
                        }
                    }
                    connection.send(content: Frame.control(["v": 1, "type": "hello", "token": pair.desktop.token]), completion: .idempotent)
                    self.receive(connection)
                case .failed(let error): self.fail(String(describing: error))
                default: break
                }
            }
        }
        connection.start(queue: .main)
    }

    private func receive(_ connection: NWConnection) {
        connection.receive(minimumIncompleteLength: 5, maximumLength: 5) { [weak self] head, _, _, error in
            guard let self, self.connection === connection else { return }
            guard let head, head.count == 5 else { return self.fail(error.map { String(describing: $0) } ?? "The PC closed the connection.") }
            let length = Int(head.withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }.littleEndian) - 1
            guard length >= 0, length <= 16 << 20 else { return self.fail("The PC sent an oversized frame.") }
            connection.receive(minimumIncompleteLength: length, maximumLength: length) { body, _, _, error in
                guard self.connection === connection else { return }
                guard let body, body.count == length else { return self.fail(error.map { String(describing: $0) } ?? "The PC closed the connection.") }
                self.handle(type: head[4], body)
                self.receive(connection)
            }
        }
    }

    private static var now: Int64 { Int64(DispatchTime.now().uptimeNanoseconds / 1000) }

    private func startTicker() {
        ticker?.cancel()
        ticker = Task {
            var tick = 0
            while !Task.isCancelled {
                connection?.send(content: Frame.control(["v": 1, "type": "clock", "t1": Self.now]), completion: .idempotent)
                tick += 1
                if tick % 3 == 0 { report() }
                try? await Task.sleep(for: .seconds(2))
            }
        }
    }

    private func report() {
        guard let best = clock.min(by: { $0.rtt < $1.rtt }), !samples.isEmpty else { return }
        let sorted = samples.sorted()
        let p50 = Double(sorted[sorted.count / 2]) / 1000, p95 = Double(sorted[min(sorted.count - 1, sorted.count * 95 / 100)]) / 1000
        latency = String(format: "%.0f/%.0f ms rtt %.1f", p50, p95, Double(best.rtt) / 1000)
        connection?.send(content: Frame.control(["v": 1, "type": "latency", "p50": p50, "p95": p95, "rtt": Double(best.rtt) / 1000, "frames": samples.count, "dropped": dropped]), completion: .idempotent)
        samples.removeAll()
        dropped = 0
    }

    private func handle(type: UInt8, _ body: Data) {
        switch type {
        case 0:
            guard body.count > 9 else { return }
            let captured = Int64(bitPattern: body.withUnsafeBytes { $0.loadUnaligned(as: UInt64.self) }.littleEndian)
            let enqueued = video.enqueue(annexB: body.dropFirst(9), keyframe: body[body.startIndex + 8] & 1 == 1)
            if !enqueued { dropped += 1 } else if let best = clock.min(by: { $0.rtt < $1.rtt }) { samples.append(Self.now - (captured + best.offset)) }
        case 1:
            guard let json = try? JSONSerialization.jsonObject(with: body) as? [String: Any], let kind = json["type"] as? String else { return }
            switch kind {
            case "stream":
                state = .streaming(width: json["width"] as? Int ?? 0, height: json["height"] as? Int ?? 0)
                startTicker()
            case "clock":
                guard let t1 = json["t1"] as? Int64, let t2 = json["t2"] as? Int64 else { return }
                let t3 = Self.now
                clock.append((rtt: t3 - t1, offset: t2 - (t1 + t3) / 2))
                if clock.count > 15 { clock.removeFirst() }
            case "immersive":
                guard immersion == .starting, let host = hostAddress, let port = (json["port"] as? Int).flatMap({ NWEndpoint.Port(rawValue: UInt16($0)) }) else { return leaveImmersive() }
                immersion = .on
                Task {
                    do { try await session.connect(endpoint: .local(ipAddress: host, port: port)) } catch { leaveImmersive() }
                }
            case "games":
                games = (try? JSONDecoder().decode([Game].self, from: JSONSerialization.data(withJSONObject: json["games"] ?? []))) ?? []
            case "game":
                guard let id = json["id"] as? String, let known = games.first(where: { $0.id == id }) else { return }
                game = json["running"] as? Bool == true ? .running(known) : (json["reason"] as? String).map { .failed($0) } ?? .none
            case "windowed":
                if immersion == .on { leaveImmersive() }
                video.reset()
                connection?.send(content: Frame.control(["v": 1, "type": "keyframe"]), completion: .idempotent)
            case "bye": disconnect()
            default: break
            }
        default: break
        }
    }

    private func fail(_ reason: String) {
        ticker?.cancel()
        connection?.cancel()
        connection = nil
        video.reset()
        state = .failed(reason)
    }
}

enum Frame {
    static func control(_ object: [String: Any]) -> Data { frame(1, try! JSONSerialization.data(withJSONObject: object)) }
    static func input(_ record: Data) -> Data { frame(2, record) }

    private static func frame(_ type: UInt8, _ body: Data) -> Data {
        var frame = Data(count: 4)
        frame.withUnsafeMutableBytes { $0.storeBytes(of: UInt32(body.count + 1).littleEndian, as: UInt32.self) }
        frame.append(type)
        frame.append(body)
        return frame
    }
}
