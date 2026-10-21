import XCTest
import FlowCore

final class TouchpadAvailabilityTests:XCTestCase {
    func testWindowsReceptionIsAdvertisedWithoutClaimingVirtualHIDOrReverseCapture() {
        XCTAssertFalse(TouchpadAvailability.forwardingEnabled)
        XCTAssertTrue(TouchpadAvailability.receivesWindowsGestures)
        XCTAssertTrue(TouchpadAvailability.displayFeatures.contains("touchpad-quartz-receive-v1"))
        XCTAssertTrue(TouchpadAvailability.displayFeatures.contains("touchpad-feedback-v1"))
        XCTAssertFalse(TouchpadAvailability.displayFeatures.contains("touchpad-native-receive-v1"))
    }
}
