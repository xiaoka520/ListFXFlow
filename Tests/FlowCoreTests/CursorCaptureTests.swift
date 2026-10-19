import XCTest
@testable import FlowMac

final class CursorCaptureTests:XCTestCase {
    private final class WindowServer {
        var background=false,hidden=false,detached=false
        var point=CGPoint(x:450,y:220)
        var calls=[String]()
        var warps=[CGPoint]()
        var succeed=true
        var driver:CursorCapture.Driver {
            .init(allowBackground:{ self.calls.append("background");self.background=true;return true },
                  associate:{ coupled in
                      self.calls.append(coupled ? "associate":"detach")
                      self.detached = !coupled && self.background
                      return true
                  },hide:{ self.calls.append("hide");self.hidden=true;return true },
                  show:{ self.calls.append("show");self.hidden=false },
                  position:{ self.point },warp:{ p in
                      self.calls.append("warp");self.warps.append(p)
                      if self.succeed { self.point=p };return self.succeed
                  })
        }
    }
    func testBackgroundCaptureAndLostAssociationRepairKeepTheSeam() {
        let ws=WindowServer(),q=DispatchQueue(label:"capture.test")
        let capture=CursorCapture(driver:ws.driver,queue:q,automaticPolling:false)
        let anchor=CGPoint(x:1919,y:411)
        capture.begin(at:anchor);q.sync {}
        XCTAssertEqual(ws.calls,["background","hide","detach","warp"])
        XCTAssertTrue(ws.detached);XCTAssertTrue(ws.hidden)
        // Another foreground app/WindowServer transition re-couples the cursor.
        q.sync { ws.background=false;ws.detached=false;ws.point=CGPoint(x:1700,y:500);capture.poll() }
        XCTAssertEqual(ws.point,anchor);XCTAssertTrue(ws.detached)
        XCTAssertEqual(ws.calls.filter{$0=="hide"}.count,1) // Balanced hide count.
        let returned=CGPoint(x:1918,y:700)
        capture.end(at:returned);q.sync { capture.poll() }
        XCTAssertEqual(ws.point,returned);XCTAssertFalse(ws.hidden);XCTAssertFalse(ws.detached)
        let count=ws.warps.count
        capture.pinIfNeeded();q.sync { capture.poll() }
        XCTAssertEqual(ws.warps.count,count) // Old movement cannot re-park after return.
        capture.finish()
    }
    func testRawMotionCoalescesWithoutCallingWindowServerAndRepairsStaleSnapshot() {
        let ws=WindowServer(),q=DispatchQueue(label:"capture.motion.test")
        let capture=CursorCapture(driver:ws.driver,queue:q,automaticPolling:false)
        let anchor=CGPoint(x:0,y:411)
        capture.begin(at:anchor);q.sync {}
        let count=ws.warps.count
        // Snapshot still equals the anchor although newer HID motion is pending.
        for _ in 0..<1000 { capture.pinIfNeeded() }
        q.sync { XCTAssertEqual(ws.warps.count,count);capture.poll() }
        XCTAssertEqual(ws.warps.count,count+1)
        q.sync { capture.poll() }
        XCTAssertEqual(ws.warps.count,count+1)
        capture.finish()
    }
    func testOldFailureCannotStopANewCapture() {
        let ws=WindowServer(),q=DispatchQueue(label:"capture.failure.test")
        let capture=CursorCapture(driver:ws.driver,queue:q,automaticPolling:false)
        var lost=false;capture.onLost={ lost=true }
        ws.succeed=false
        capture.begin(at:CGPoint(x:0,y:100))
        q.sync { capture.poll();capture.poll() } // Queues a main-thread failure.
        capture.end();q.sync { ws.succeed=true }
        capture.begin(at:CGPoint(x:1919,y:300));q.sync {}
        let drained=expectation(description:"main callbacks drained")
        DispatchQueue.main.async { drained.fulfill() }
        wait(for:[drained],timeout:1)
        XCTAssertFalse(lost);XCTAssertEqual(ws.point,CGPoint(x:1919,y:300))
        capture.finish()
    }
}
