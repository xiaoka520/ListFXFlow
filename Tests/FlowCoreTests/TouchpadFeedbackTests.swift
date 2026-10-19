import XCTest
import FlowCore
@testable import FlowMac

final class TouchpadFeedbackTests:XCTestCase {
    private let pair=[TouchpadContact(id:0,x:0.3,y:0.5),TouchpadContact(id:1,x:0.7,y:0.5)]
    private func frame(_ phase:String,_ epoch:Int64=3,_ delta:Double=0)->Message {
        var m=Message("touchpad");m.gestureEpoch=epoch;m.phase=phase
        m.contacts=["end","cancel"].contains(phase) ? []:pair.map { .init(id:$0.id,x:$0.x+delta,y:$0.y) }
        return m
    }
    func testFeedbackSeparatesReceiptFromSubmissionAndRejectsOldStroke() throws {
        var submitted=[NativeTouchpadGesture]()
        let receiver=MacGestureReceiver { submitted.append($0);return true }
        let begin=try XCTUnwrap(receiver.receive(frame("begin"),at:1))
        XCTAssertEqual(begin.phase,"received");XCTAssertEqual(begin.events,0)
        XCTAssertTrue(submitted.isEmpty)
        XCTAssertNil(receiver.receive(frame("change",2,0.04),at:1.3))
        XCTAssertTrue(submitted.isEmpty)
        let changed=try XCTUnwrap(receiver.receive(frame("change",3,0.04),at:1.4))
        XCTAssertEqual(changed.phase,"submitted");XCTAssertEqual(changed.frames,2)
        XCTAssertEqual(changed.events,Int64(submitted.count));XCTAssertEqual(changed.backend,"quartz")
        try changed.validate()
        let end=try XCTUnwrap(receiver.receive(frame("end"),at:1.41))
        XCTAssertEqual(end.frames,3);XCTAssertEqual(submitted.last?.phase,"end")
        receiver.reset()
        XCTAssertNil(receiver.receive(frame("change",3,0.05),at:2))
    }
    func testFailedNativeSubmissionIsNotAcknowledgedAsSuccess() throws {
        let receiver=MacGestureReceiver { _ in false }
        _=receiver.receive(frame("begin"),at:1)
        let changed=try XCTUnwrap(receiver.receive(frame("change",3,0.04),at:1.02))
        XCTAssertEqual(changed.phase,"unavailable");XCTAssertEqual(changed.events,0)
        XCTAssertEqual(receiver.receive(frame("end"),at:1.03)?.phase,"unavailable")
    }
    func testFeedbackThrottlePreservesEndAndFreshStrokeResetsCounters() throws {
        let receiver=MacGestureReceiver { _ in true }
        _=receiver.receive(frame("begin"),at:1)
        XCTAssertNil(receiver.receive(frame("change",3,0.04),at:1.01))
        let end=try XCTUnwrap(receiver.receive(frame("cancel"),at:1.02))
        XCTAssertEqual(end.frames,3);XCTAssertGreaterThan(end.events ?? 0,0)
        let fresh=try XCTUnwrap(receiver.receive(frame("begin",4),at:1.03))
        XCTAssertEqual(fresh.frames,1);XCTAssertEqual(fresh.events,0)
    }
    func testMalformedFeedbackCannotEnterEncryptedProtocol() throws {
        var feedback=Message("touchpadFeedback");feedback.gestureEpoch=3;feedback.frames=1;feedback.events=0;feedback.phase="received";feedback.backend="quartz"
        try feedback.validate()
        XCTAssertEqual(try JSONDecoder().decode(Message.self,from:JSONEncoder().encode(feedback)),feedback)
        feedback.gestureEpoch = -1;XCTAssertThrowsError(try feedback.validate());feedback.gestureEpoch=3
        feedback.frames=1_000_000_001;XCTAssertThrowsError(try feedback.validate());feedback.frames=1
        feedback.backend="arbitrary";XCTAssertThrowsError(try feedback.validate());feedback.backend="quartz"
        feedback.phase="accepted";XCTAssertThrowsError(try feedback.validate())
    }
}
