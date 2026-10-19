import Foundation
import CryptoKit

public enum FlowError: Error { case invalidFrame, authentication, sequence, invalidMessage }

public struct Message: Codable, Equatable {
    public var type: String
    public var active: String?
    public var x: Double?
    public var y: Double?
    public var dx: Double?
    public var dy: Double?
    public var key: Int?
    public var button: Int?
    public var down: Bool?
    public var width: Double?
    public var height: Double?
    public var side: String?
    public var offset: Double?
    public var nonce: String?
    public var auth: String?
    public var version: Int?
    public var publicKey: String?
    public var features: [String]?
    public var contacts: [TouchpadContact]?
    public var phase: String?
    public var gestureEpoch:Int64?
    public var frames:Int64?
    public var events:Int64?
    public var backend:String?
    public init(_ type: String) { self.type = type }

    public func validate() throws {
        let finite = [x,y,dx,dy,width,height,offset].compactMap { $0 }.allSatisfy { $0.isFinite }
        guard finite else { throw FlowError.invalidMessage }
        switch type {
        case "pointer":
            guard ["mac", "windows"].contains(active ?? ""), let x, let y,
                  (0...1).contains(x), (0...1).contains(y) else { throw FlowError.invalidMessage }
        case "key": guard let key, KeyMap.entries.contains(where: { $0.hid == key }), down != nil else { throw FlowError.invalidMessage }
        case "button": guard let button, (0...4).contains(button), down != nil else { throw FlowError.invalidMessage }
        case "caps": guard down != nil else { throw FlowError.invalidMessage }
        case "gameModeRequest": guard down != nil else { throw FlowError.invalidMessage }
        case "gameMode":
            guard active=="windows",let x,let y,(0...1).contains(x),(0...1).contains(y),
                  (down==true && ["automatic","manual"].contains(phase ?? "")) || (down==false && phase=="off") else { throw FlowError.invalidMessage }
        case "touchpadControl": guard down != nil else { throw FlowError.invalidMessage }
        case "touchpad":
            guard gestureEpoch.map({ $0>=0 }) ?? true,let contacts,contacts.count<=5,Set(contacts.map(\.id)).count==contacts.count,
                  contacts.allSatisfy({ $0.valid }),["begin","change","end","cancel"].contains(phase ?? ""),
                  (phase=="begin" || phase=="change" ? contacts.count>=2:contacts.isEmpty) else { throw FlowError.invalidMessage }
        case "touchpadFeedback":
            guard let gestureEpoch,gestureEpoch>=0,let frames,(0...1_000_000_000).contains(frames),let events,(0...1_000_000_000).contains(events),
                  ["received","submitted","unavailable"].contains(phase ?? ""),["quartz","virtual-hid","synthetic-touchpad"].contains(backend ?? "") else { throw FlowError.invalidMessage }
        case "scroll": guard let dx, let dy, abs(dx) <= 10000, abs(dy) <= 10000 else { throw FlowError.invalidMessage }
        case "relativePointer": guard let dx,let dy,abs(dx)<=1000000,abs(dy)<=1000000 else { throw FlowError.invalidMessage }
        case "display": guard let width, let height, (320...32768).contains(width), (200...32768).contains(height) else { throw FlowError.invalidMessage }
        case "layout": guard Side(rawValue: side ?? "") != nil, let offset, abs(offset) <= 900 else { throw FlowError.invalidMessage }
        case "clipboardReady": guard let nonce, Data(hex: nonce)?.count == 32 else { throw FlowError.invalidMessage }
        case "ping", "reset": break
        default: throw FlowError.invalidMessage
        }
    }
}

public extension Data {
    init?(hex: String) {
        guard hex.count % 2 == 0 else { return nil }
        var bytes: [UInt8] = []; var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2)
            guard let byte = UInt8(hex[index..<next], radix: 16) else { return nil }
            bytes.append(byte); index = next
        }
        self.init(bytes)
    }
    var hex: String { map { String(format: "%02x", $0) }.joined() }
    static func random(_ count: Int) -> Data {
        var generator = SystemRandomNumberGenerator()
        return Data((0..<count).map { _ in UInt8.random(in: 0...255, using: &generator) })
    }
}

public enum Authentication {
    public static func sharedSecret(privateKey: P256.KeyAgreement.PrivateKey, peer: Data) throws -> Data {
        let key = try P256.KeyAgreement.PublicKey(x963Representation: peer)
        let shared = try privateKey.sharedSecretFromKeyAgreement(with: key)
        return shared.withUnsafeBytes { Data(SHA256.hash(data: Data("ListFXFlow/1/ECDH".utf8) + Data($0))) }
    }
    public static func proof(secret: Data, role: String, client: Data, server: Data) -> Data {
        Data(HMAC<SHA256>.authenticationCode(for: Data(("ListFXFlow/1/" + role).utf8) + client + server, using: SymmetricKey(data: secret)))
    }
    public static func verify(_ proof: Data, secret: Data, role: String, client: Data, server: Data) -> Bool {
        HMAC<SHA256>.isValidAuthenticationCode(proof, authenticating: Data(("ListFXFlow/1/" + role).utf8) + client + server, using: SymmetricKey(data: secret))
    }
    public static func key(secret: Data, direction: String, client: Data, server: Data) -> Data {
        proof(secret: secret, role: direction, client: client, server: server)
    }
}

/// Direction-specific keys, monotonically increasing nonces; never reuse across sessions.
public final class SecureChannel {
    private let sendKey: SymmetricKey
    private let receiveKey: SymmetricKey
    private var sent: UInt64 = 0
    private var received: UInt64 = 0
    public init(secret: Data, client: Data, server: Data, isServer: Bool) {
        sendKey = SymmetricKey(data: Authentication.key(secret: secret, direction: isServer ? "s2c" : "c2s", client: client, server: server))
        receiveKey = SymmetricKey(data: Authentication.key(secret: secret, direction: isServer ? "c2s" : "s2c", client: client, server: server))
    }
    private func nonce(_ sequence: UInt64) -> Data {
        var big = sequence.bigEndian
        return Data(repeating: 0, count: 4) + withUnsafeBytes(of: &big) { Data($0) }
    }
    public func seal(_ message: Message) throws -> Data {
        try message.validate()
        return try sealData(JSONEncoder().encode(message))
    }
    public func sealData(_ data: Data) throws -> Data {
        guard sent < UInt64.max else { throw FlowError.sequence }; sent += 1
        let sealed = try AES.GCM.seal(data, using: sendKey, nonce: AES.GCM.Nonce(data: nonce(sent)), authenticating: Data("ListFXFlow/1".utf8))
        return sealed.combined!
    }
    public func open(_ payload: Data) throws -> Message {
        let message = try JSONDecoder().decode(Message.self, from: openData(payload))
        try message.validate(); return message
    }
    public func openData(_ payload: Data) throws -> Data {
        guard payload.count >= 28, received < UInt64.max, payload.prefix(12) == nonce(received + 1) else { throw FlowError.sequence }
        let data = try AES.GCM.open(AES.GCM.SealedBox(combined: payload), using: receiveKey, authenticating: Data("ListFXFlow/1".utf8))
        received += 1; return data
    }
}

public struct FrameDecoder {
    public static let limit = 16384
    private var buffer = Data()
    public init() {}
    public mutating func append(_ bytes: Data) throws -> [Data] {
        buffer.append(bytes); var result: [Data] = []
        while buffer.count >= 4 {
            let length = buffer.prefix(4).reduce(0) { ($0 << 8) | Int($1) }
            guard length > 0 && length <= Self.limit else { throw FlowError.invalidFrame }
            guard buffer.count >= length + 4 else { break }
            result.append(Data(buffer.dropFirst(4).prefix(length)))
            buffer.removeFirst(length + 4)
        }
        guard buffer.count <= Self.limit + 4 else { throw FlowError.invalidFrame }
        return result
    }
    public static func frame(_ payload: Data) -> Data {
        var length = UInt32(payload.count).bigEndian
        return withUnsafeBytes(of: &length) { Data($0) } + payload
    }
}
