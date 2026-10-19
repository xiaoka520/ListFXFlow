import Foundation
import CryptoKit
import Security
import Darwin

enum IdentityStore {
    static func read(_ account: String) -> Data? {
        var result: CFTypeRef?
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "com.listfxflow.identity", kSecAttrAccount as String: account,
            kSecReturnData as String: true]
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }
    static func write(_ data: Data, account: String) throws {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "com.listfxflow.identity", kSecAttrAccount as String: account]
        let status = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if status == errSecItemNotFound {
            var item = query; item[kSecValueData as String] = data
            item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            guard SecItemAdd(item as CFDictionary, nil) == errSecSuccess else { throw FlowMacError.identity }
        } else if status != errSecSuccess { throw FlowMacError.identity }
    }
    static func key() throws -> P256.KeyAgreement.PrivateKey {
        if let data = read("private-key") { return try P256.KeyAgreement.PrivateKey(rawRepresentation: data) }
        let key = P256.KeyAgreement.PrivateKey(); try write(key.rawRepresentation, account: "private-key")
        return key
    }
}

enum FlowMacError: LocalizedError {
    case identity, bridge, permission, changedPeer, protocolError
    var errorDescription: String? {
        switch self {
        case .identity: return "无法访问设备身份，请检查钥匙串权限。"
        case .bridge: return "尚未发现已分配 IPv4 地址的雷雳网桥。请连接雷雳线并检查系统网络设置。"
        case .permission: return "请在系统设置中允许 ListFXFlow 使用辅助功能与输入监控，然后重新启用。"
        case .changedPeer: return "连接设备的身份发生变化。确认换机后需清理本机保存的配对信息。"
        case .protocolError: return "连接校验失败，已恢复本机控制。"
        }
    }
}

struct BridgeAddress: Identifiable {
    var name: String; var address: String; var broadcast: String
    var mask: String = "255.255.255.0"
    var id: String { "\(name):\(address)/\(mask)" }
    static func all() -> [BridgeAddress] {
        var pointer: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&pointer) == 0 else { return [] }
        defer { freeifaddrs(pointer) }
        var values: [BridgeAddress] = []; var current = pointer
        while let item = current {
            defer { current = item.pointee.ifa_next }
            let entry = item.pointee
            guard let address = entry.ifa_addr, address.pointee.sa_family == UInt8(AF_INET),
                  let mask = entry.ifa_netmask else { continue }
            let name = String(cString: entry.ifa_name)
            guard name == "bridge0", entry.ifa_flags & UInt32(IFF_UP) != 0 else { continue }
            let ip = UnsafeRawPointer(address).assumingMemoryBound(to: sockaddr_in.self).pointee.sin_addr.s_addr
            let netmask = UnsafeRawPointer(mask).assumingMemoryBound(to: sockaddr_in.self).pointee.sin_addr.s_addr
            func string(_ value: UInt32) -> String { var a = in_addr(s_addr: value); var chars = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN)); inet_ntop(AF_INET, &a, &chars, socklen_t(chars.count)); return String(cString: chars) }
            values.append(BridgeAddress(name: name, address: string(ip), broadcast: string(ip | ~netmask), mask: string(netmask)))
        }
        return values.sorted { $0.id < $1.id }
    }
}
