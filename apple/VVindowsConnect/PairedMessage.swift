import Foundation

struct PairedMessage: Decodable {
    let v: Int
    let type: String
    let serverId: String
    let hostName: String

    init(_ data: Data) throws {
        self = try JSONDecoder().decode(Self.self, from: data)
        guard v == 1, type == "paired" else { throw PairingError.unexpectedMessage }
    }
}
