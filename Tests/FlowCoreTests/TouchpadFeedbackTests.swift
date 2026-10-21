import XCTest
import FlowCore
import CoreGraphics
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
    func testWindowsThreeFingerStrokeProducesNativeDockSequenceAndCancelsOnReset() throws {
        var submitted=[CGEvent]()
        let receiver=MacGestureReceiver { gesture in
            guard let events=NativeTouchpad.events(gesture,at:.zero,augmentDock:true) else{return false}
            submitted.append(contentsOf:events);return true
        }
        var m=Message("touchpad");m.phase="begin";m.gestureEpoch=41
        m.contacts=[.init(id:0,x:0.2,y:0.5),.init(id:1,x:0.5,y:0.5),.init(id:2,x:0.8,y:0.5)]
        _=receiver.receive(m,at:1)
        XCTAssertTrue(submitted.isEmpty)
        m.phase="change";m.contacts=m.contacts!.map{.init(id:$0.id,x:$0.x-0.1,y:$0.y)}
        let feedback=try XCTUnwrap(receiver.receive(m,at:1.3))
        XCTAssertEqual(feedback.phase,"submitted");XCTAssertEqual(feedback.events,2)
        XCTAssertEqual(submitted.map{$0.type.rawValue},[30,29,30,29])
        XCTAssertEqual(submitted[2].getDoubleValueField(CGEventField(rawValue:124)!),-0.1,accuracy:0.0001)
        receiver.reset()
        XCTAssertEqual(submitted.count,6)
        XCTAssertEqual(submitted[4].getIntegerValueField(CGEventField(rawValue:132)!),8)
        XCTAssertNil(receiver.receive(m,at:1.4))
        XCTAssertEqual(submitted.count,6)
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
    func testRawSourceClockControlsVelocityInsteadOfNetworkArrivalSpacing() throws {
        var gestures=[NativeTouchpadGesture]()
        let receiver=MacGestureReceiver { gestures.append($0);return true }
        var m=Message("touchpad");m.phase="begin";m.gestureEpoch=42;m.gestureTime=100
        m.contacts=[.init(id:0,x:0.2,y:0.5),.init(id:1,x:0.5,y:0.5),.init(id:2,x:0.8,y:0.5)]
        _=receiver.receive(m,at:1)
        m.phase="change";m.gestureTime=100.05;m.contacts=m.contacts!.map{.init(id:$0.id,x:$0.x-0.03,y:$0.y)}
        _=receiver.receive(m,at:10) // Deliberately unrelated network delay.
        var end=Message("touchpad");end.phase="end";end.gestureEpoch=42;end.gestureTime=100.06;end.contacts=[]
        _=receiver.receive(end,at:20)
        XCTAssertEqual(try XCTUnwrap(gestures.last).velocity,39,accuracy:0.001)
        m.phase="begin";m.gestureTime=200;_=receiver.receive(m,at:21)
        m.phase="change";m.gestureTime=200.05;m.contacts=m.contacts!.map{.init(id:$0.id,x:$0.x-0.03,y:$0.y)}
        _=receiver.receive(m,at:22)
        m.gestureTime=200.01
        XCTAssertNil(receiver.receive(m,at:22.1))
        XCTAssertEqual(gestures.last?.phase,"cancel")
        m.gestureTime = -1;XCTAssertThrowsError(try m.validate())
        m.gestureTime = .nan;XCTAssertThrowsError(try m.validate())
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
