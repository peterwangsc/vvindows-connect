import Foundation
import Security

struct SavedPair: Codable, Equatable {
    let serverId: String
    let hostName: String
    let desktop: PairedMessage.Desktop
    let pairedAt: Date

    init(_ message: PairedMessage) {
        serverId = message.serverId
        hostName = message.hostName
        desktop = message.desktop
        pairedAt = .now
    }

    private static var item: [CFString: Any] { [
        kSecClass: kSecClassGenericPassword,
        kSecAttrService: "com.golfcore.vvindowsconnect",
        kSecAttrAccount: "pair",
    ] }

    static func load() -> SavedPair? {
        var result: CFTypeRef?
        guard SecItemCopyMatching(item.merging([kSecReturnData: true]) { $1 } as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data else { return nil }
        return try? JSONDecoder().decode(SavedPair.self, from: data)
    }

    func saved() throws -> SavedPair {
        Self.delete()
        let status = SecItemAdd(Self.item.merging([kSecValueData: try JSONEncoder().encode(self)]) { $1 } as CFDictionary, nil)
        guard status == errSecSuccess else { throw KeychainError(status: status) }
        return self
    }

    static func delete() { SecItemDelete(item as CFDictionary) }
}

struct KeychainError: Error, LocalizedError {
    let status: OSStatus
    var errorDescription: String? { SecCopyErrorMessageString(status, nil) as String? ?? "Keychain error \(status)" }
}
