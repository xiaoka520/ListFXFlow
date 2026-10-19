import XCTest
import FlowCore

final class AddressPreferenceTests:XCTestCase {
    func testSavedAddressWinsOnlyWhenItIsActuallyAvailable() {
        XCTAssertEqual(AddressPreference.ordered(["192.168.3.1","10.0.0.2"],preferred:"10.0.0.2"),["10.0.0.2","192.168.3.1"])
        XCTAssertEqual(AddressPreference.ordered(["192.168.3.1"],preferred:"10.0.0.2"),["192.168.3.1"])
        XCTAssertEqual(AddressPreference.ordered([],preferred:"10.0.0.2"),[])
        XCTAssertEqual(AddressPreference.ordered(["10.0.0.2"],preferred:nil),["10.0.0.2"])
    }
}
