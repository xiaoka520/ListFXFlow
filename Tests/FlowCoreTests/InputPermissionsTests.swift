import XCTest
@testable import FlowCore

final class InputPermissionsTests: XCTestCase {
    func testMonitoringCanBeRequestedWhileAccessibilityIsDenied() {
        var accessibilityPrompts = 0, monitoringPrompts = 0
        let permissions = InputPermissions(readAccessibility: { false }, readMonitoring: { false },
            requestAccessibility: { accessibilityPrompts += 1 }, requestMonitoring: { monitoringPrompts += 1 })
        permissions.request(.monitoring)
        XCTAssertEqual(monitoringPrompts, 1); XCTAssertEqual(accessibilityPrompts, 0)
        permissions.request(.accessibility)
        XCTAssertEqual(accessibilityPrompts, 1)
        XCTAssertFalse(permissions.status.ready)
    }
    func testAsyncGrantRefreshAndRevocation() {
        var accessibility = false, monitoring = false
        let permissions = InputPermissions(readAccessibility: { accessibility }, readMonitoring: { monitoring },
            requestAccessibility: {}, requestMonitoring: {})
        permissions.request(.accessibility)
        XCTAssertFalse(permissions.status.accessibility)
        accessibility = true; permissions.refresh()
        XCTAssertTrue(permissions.status.accessibility); XCTAssertFalse(permissions.status.ready)
        monitoring = true; permissions.refresh(); XCTAssertTrue(permissions.status.ready)
        accessibility = false; permissions.refresh(); XCTAssertFalse(permissions.status.ready)
    }
    func testAlreadyGrantedPermissionsDoNotPromptAgain() {
        var prompts = 0
        let permissions = InputPermissions(readAccessibility: { true }, readMonitoring: { true },
            requestAccessibility: { prompts += 1 }, requestMonitoring: { prompts += 1 })
        permissions.request(.accessibility); permissions.request(.monitoring); permissions.refresh()
        XCTAssertTrue(permissions.status.ready); XCTAssertEqual(prompts, 0)
    }
}
