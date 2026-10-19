import XCTest
import FlowCore

final class TouchpadAvailabilityTests:XCTestCase {
    func testHiddenForwardingDoesNotAdvertiseNativeReception() {
        XCTAssertFalse(TouchpadAvailability.forwardingEnabled)
        XCTAssertEqual(TouchpadAvailability.displayFeatures,["clipboard-v1","caps-v1","game-mode-v1"])
        XCTAssertFalse(TouchpadAvailability.displayFeatures.contains("touchpad-native-receive-v1"))
    }
}
