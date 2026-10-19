import XCTest
import CryptoKit
@testable import FlowCore

final class FlowCoreTests: XCTestCase {
    func testFragmentedAndCoalescedFrames() throws {
        let data = FrameDecoder.frame(Data("one".utf8)) + FrameDecoder.frame(Data("two".utf8))
        for cut in 0...data.count {
            var decoder = FrameDecoder()
            let a = try decoder.append(Data(data.prefix(cut)))
            let b = try decoder.append(Data(data.dropFirst(cut)))
            XCTAssertEqual((a+b).map { String(data: $0, encoding: .utf8)! }, ["one","two"])
        }
    }
    func testRejectsOversizedAndZeroLength() {
        for bytes: [UInt8] in [[0,0,0,0],[0,1,0,0],[255,255,255,255]] {
            var decoder = FrameDecoder(); XCTAssertThrowsError(try decoder.append(Data(bytes)))
        }
    }
    func testAuthenticatedDuplexReplayTamperAndWrongIdentity() throws {
        let secret = Data(repeating: 7, count: 32), c = Data(repeating: 1,count: 32), s = Data(repeating: 2,count: 32)
        let client = SecureChannel(secret: secret, client: c, server: s, isServer: false)
        let server = SecureChannel(secret: secret, client: c, server: s, isServer: true)
        let frame = try client.seal(Message("ping"))
        XCTAssertEqual(try server.open(frame).type, "ping")
        XCTAssertThrowsError(try server.open(frame))
        XCTAssertEqual(try client.open(server.seal(Message("reset"))).type,"reset")
        var tamper = try client.seal(Message("ping")); tamper[tamper.count-1] ^= 1
        XCTAssertThrowsError(try server.open(tamper))
        let stranger = SecureChannel(secret: .random(32),client: c,server: s,isServer: true)
        XCTAssertThrowsError(try stranger.open(frame))
        let differentSession = SecureChannel(secret: secret,client: .random(32),server: s,isServer: true)
        XCTAssertThrowsError(try differentSession.open(frame))
    }
    func testECDHAndRoleProofs() throws {
        let a = P256.KeyAgreement.PrivateKey(), b = P256.KeyAgreement.PrivateKey()
        let ab = try Authentication.sharedSecret(privateKey: a,peer: b.publicKey.x963Representation)
        let ba = try Authentication.sharedSecret(privateKey: b,peer: a.publicKey.x963Representation)
        XCTAssertEqual(ab,ba)
        let c = Data.random(32), s = Data.random(32)
        let proof = Authentication.proof(secret: ab,role: "client",client: c,server: s)
        XCTAssertTrue(Authentication.verify(proof,secret: ba,role: "client",client: c,server: s))
        XCTAssertFalse(Authentication.verify(proof,secret: ba,role: "server",client: c,server: s))
    }
    func testFourSidesAndReturnWithEitherSource() {
        for side in Side.allCases {
            let router = PointerRouter(); router.topology.side = side
            let edge: (Double,Double,Double,Double) = switch side {
            case .left: (0,500,-10,0)
            case .right: (1599,500,10,0)
            case .top: (800,0,0,-10)
            case .bottom: (800,999,0,10)
            }
            let m = router.move(local: "mac",x: edge.0,y: edge.1,dx: edge.2,dy: edge.3,width: 1600,height: 1000)
            XCTAssertEqual(m.active,"windows",side.rawValue)
            let reverse = router.move(local: "mac",x: 0,y: 0,dx: -edge.2*4,dy: -edge.3*4,width: 1600,height: 1000)
            XCTAssertEqual(reverse.active,"mac",side.rawValue)
            router.reset(local: "windows")
            let winEdge: (Double,Double,Double,Double) = switch side {
            case .left: (1599,500,10,0)
            case .right: (0,500,-10,0)
            case .top: (800,999,0,10)
            case .bottom: (800,0,0,-10)
            }
            XCTAssertEqual(router.move(local: "windows",x: winEdge.0,y: winEdge.1,dx: winEdge.2,dy: winEdge.3,width: 1600,height: 1000).active,"mac",side.rawValue)
        }
    }
    func testGapAndHeldKeyDoNotSwitch() {
        let r = PointerRouter(); r.topology.offset = 800
        XCTAssertEqual(r.move(local: "mac",x: 1599,y: 200,dx: 10,dy: 0,width: 1600,height: 1000).active,"mac")
        XCTAssertEqual(r.move(local: "mac",x: 1599,y: 900,dx: 10,dy: 0,width: 1600,height: 1000,mayCross: false).active,"mac")
        XCTAssertEqual(r.move(local: "mac",x: 1599,y: 900,dx: 10,dy: 0,width: 1600,height: 1000).active,"windows")
    }
    func testLargeDeltaCannotSkipTheReturnSeam() {
        let r = PointerRouter()
        _ = r.move(local: "mac",x: 1599,y: 500,dx: 10,dy: 0,width: 1600,height: 1000)
        let m = r.move(local: "mac",x: 0,y: 0,dx: -20000,dy: 0,width: 1600,height: 1000)
        XCTAssertEqual(m.active,"mac"); XCTAssertGreaterThanOrEqual(m.x!,0)
    }
    func testInvalidInputNeverReachesInjection() {
        var p = Message("pointer"); p.active = "mac"; p.x = .nan; p.y = 0
        XCTAssertThrowsError(try p.validate())
        p.x = 2; XCTAssertThrowsError(try p.validate())
        var k = Message("key"); k.key = 999; k.down = true; XCTAssertThrowsError(try k.validate())
        var l = Message("layout"); l.side = "right"; l.offset = 901; XCTAssertThrowsError(try l.validate())
    }
    func testCapsStateMessageValidation() throws {
        var caps = Message("caps"); caps.down = true
        XCTAssertNoThrow(try caps.validate())
        caps.down = nil
        XCTAssertThrowsError(try caps.validate())
    }
    func testKeyMapHasUniquePhysicalCodes() {
        XCTAssertEqual(Set(KeyMap.entries.map(\.hid)).count,KeyMap.entries.count)
        XCTAssertEqual(Set(KeyMap.entries.map(\.mac)).count,KeyMap.entries.count)
        XCTAssertEqual(Set(KeyMap.entries.map { $0.scan | ($0.extended ? 0x100 : 0) }).count,KeyMap.entries.count)
        XCTAssertEqual(KeyMap.byMac[0],4); XCTAssertEqual(KeyMap.byHID[227],55)
    }
}

final class NetworkDeliveryTests: XCTestCase {
    func testOldSessionMessagesCannotReachAReconnectedDesktop() {
        let gate=MessageDeliveryGate(capacity:2)
        let old=gate.reserve()!
        XCTAssertNotNil(gate.reserve())
        XCTAssertNil(gate.reserve())
        gate.advance()
        XCTAssertFalse(gate.valid(old))
        gate.release();gate.release()
        let current=gate.reserve()!
        XCTAssertTrue(gate.valid(current))
        XCTAssertNotEqual(current,old)
        gate.release()
    }
}
