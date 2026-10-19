import XCTest
import CryptoKit
import FlowCore

final class LoginConfigurationTests:XCTestCase {
    func testLoginSupportRequiresAValidPinnedPeerAndBoundedLayout() throws {
        var config=LoginConfiguration(identity:P256.KeyAgreement.PrivateKey().rawRepresentation,
            peer:P256.KeyAgreement.PrivateKey().publicKey.x963Representation,side:.right,offset:0)
        XCTAssertNoThrow(try config.validate())
        let encoded=try JSONEncoder().encode(config)
        XCTAssertEqual(try JSONDecoder().decode(LoginConfiguration.self,from:encoded).peer,config.peer)
        config.offset=Double.infinity;XCTAssertThrowsError(try config.validate())
        config.offset=901;XCTAssertThrowsError(try config.validate())
        config.offset=0;config.peer=Data();XCTAssertThrowsError(try config.validate())
        config.peer=Data([4])+Data(repeating:0,count:64);XCTAssertThrowsError(try config.validate())
    }
}
