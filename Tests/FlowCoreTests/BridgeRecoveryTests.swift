import XCTest
import FlowCore

final class BridgeRecoveryTests:XCTestCase {
    func testBootWaitsForAddressAndRebindsWhenSharingChangesIt() {
        var recovery=BridgeRecovery();recovery.request()
        XCTAssertEqual(recovery.action(addresses:[],listening:false,now:0),.waitForAddress)
        XCTAssertEqual(recovery.action(addresses:["192.168.3.1"],listening:false,now:2),.start)
        recovery.started(addresses:["192.168.3.1"])
        XCTAssertEqual(recovery.action(addresses:["192.168.3.1"],listening:true,now:3),.none)
        XCTAssertEqual(recovery.action(addresses:[],listening:true,now:4),.waitForAddress)
        XCTAssertEqual(recovery.action(addresses:["10.0.0.2"],listening:true,now:5),.start)
        recovery.started(addresses:["10.0.0.2"])
        XCTAssertEqual(recovery.action(addresses:["10.0.0.2","192.168.3.1"],listening:true,now:6,activeAddress:"10.0.0.2"),.none)
        XCTAssertEqual(recovery.action(addresses:["192.168.3.1"],listening:true,now:6,activeAddress:"10.0.0.2"),.start)
        XCTAssertEqual(recovery.action(addresses:["10.0.0.2","169.254.1.2"],listening:true,now:6),.start)
    }
    func testListenerFailureRetriesButManualStopStaysStopped() {
        var recovery=BridgeRecovery();recovery.request();recovery.started(addresses:["10.0.0.2"])
        recovery.failed(now:10)
        XCTAssertEqual(recovery.action(addresses:["10.0.0.2"],listening:false,now:10.5),.none)
        XCTAssertEqual(recovery.action(addresses:["10.0.0.2"],listening:false,now:11),.start)
        recovery.stop()
        XCTAssertEqual(recovery.action(addresses:[],listening:false,now:12),.none)
        XCTAssertEqual(recovery.action(addresses:["10.0.0.2"],listening:false,now:20),.none)
    }
}
