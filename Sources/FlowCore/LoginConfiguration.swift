import Foundation
import CryptoKit

public struct LoginConfiguration:Codable {
    public var identity:Data
    public var peer:Data
    public var side:Side
    public var offset:Double
    public var preferredAddress:String?
    public var desktopUID:UInt32?
    public init(identity:Data,peer:Data,side:Side,offset:Double) { self.identity=identity;self.peer=peer;self.side=side;self.offset=offset }
    public func validate() throws {
        guard identity.count==32,peer.count==65,peer.first==4,offset.isFinite,abs(offset)<=900 else { throw CocoaError(.fileReadCorruptFile) }
        _ = try P256.KeyAgreement.PrivateKey(rawRepresentation:identity)
        _ = try P256.KeyAgreement.PublicKey(x963Representation:peer)
    }
}
