import Foundation

struct PairedMessage: Decodable {
    struct Desktop: Codable, Equatable {
        let sha256: String
        let token: String
    }

    let v: Int
    let type: String
    let serverId: String
    let hostName: String
    let desktop: Desktop

    init(_ data: Data) throws {
        self = try JSONDecoder().decode(Self.self, from: data)
        guard v == 2, type == "paired", desktop.sha256.count == 64, desktop.token.count == 64 else { throw PairingError.unexpectedMessage }
    }
}
