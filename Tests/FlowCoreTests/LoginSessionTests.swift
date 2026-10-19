import XCTest
import FlowCore

final class LoginSessionTests:XCTestCase {
    func testMissingConsoleUserStillMeansPreLogin() {
        XCTAssertFalse(LoginSession.isLoggedIn(name:nil,uid:0))
        XCTAssertFalse(LoginSession.isLoggedIn(name:"loginwindow",uid:0))
        XCTAssertFalse(LoginSession.isLoggedIn(name:"_mbsetupuser",uid:501))
        XCTAssertTrue(LoginSession.isLoggedIn(name:"owner",uid:501))
    }
    func testStoppingKeepsTheActualStartupBlocker() throws {
        let date=Date(timeIntervalSince1970:1000)
        var state=LoginStatus(phase:.waitingBridge,detail:"Waiting for bridge",uptime:15,pid:100,recordedAt:date)
        state.update(.stopped,detail:"Desktop takes over",uptime:30,now:date.addingTimeInterval(15))
        state.update(.stopped,detail:"Process exiting",uptime:31,now:date.addingTimeInterval(16))
        let restored=try JSONDecoder().decode(LoginStatus.self,from:JSONEncoder().encode(state))
        XCTAssertEqual(restored.previousPhase,.waitingBridge)
        XCTAssertEqual(restored.previousDetail,"Waiting for bridge")
        XCTAssertTrue(restored.fresh(uptime:32,date:date.addingTimeInterval(17)))
        XCTAssertFalse(restored.fresh(uptime:2,date:date.addingTimeInterval(18)))
        XCTAssertFalse(restored.fresh(uptime:32,date:date.addingTimeInterval(100)))
    }
}
