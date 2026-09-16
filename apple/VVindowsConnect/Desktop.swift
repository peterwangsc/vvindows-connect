import CryptoKit
import Foundation
import Network
import Observation

@MainActor @Observable
final class Desktop {
    enum State: Equatable { case idle, connecting, streaming(width: Int, height: Int), failed(String) }

    private(set) var state = State.idle
    var windowOpen = false
    let video = VideoStream()
    private var connection: NWConnection?
    private var browser: NWBrowser?

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

    func disconnect() {
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

    private func handle(type: UInt8, _ body: Data) {
        switch type {
        case 0:
            guard body.count > 9 else { return }
            video.enqueue(annexB: body.dropFirst(9), keyframe: body[body.startIndex + 8] & 1 == 1)
        case 1:
            guard let json = try? JSONSerialization.jsonObject(with: body) as? [String: Any], let kind = json["type"] as? String else { return }
            switch kind {
            case "stream": state = .streaming(width: json["width"] as? Int ?? 0, height: json["height"] as? Int ?? 0)
            case "bye": disconnect()
            default: break
            }
        default: break
        }
    }

    private func fail(_ reason: String) {
        connection?.cancel()
        connection = nil
        video.reset()
        state = .failed(reason)
    }
}

enum Frame {
    static func control(_ object: [String: Any]) -> Data {
        let body = try! JSONSerialization.data(withJSONObject: object)
        var frame = Data(count: 4)
        frame.withUnsafeMutableBytes { $0.storeBytes(of: UInt32(body.count + 1).littleEndian, as: UInt32.self) }
        frame.append(1)
        frame.append(body)
        return frame
    }
}
