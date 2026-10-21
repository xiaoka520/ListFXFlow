import XCTest
import AppKit
import FlowCore
@testable import FlowMac

final class TouchpadTests:XCTestCase {
    private func frame(_ phase:String,_ points:[TouchpadContact])->Message {
        var m=Message("touchpad");m.phase=phase;m.contacts=points;return m
    }
    private let pair=[TouchpadContact(id:1,x:0.3,y:0.5),TouchpadContact(id:2,x:0.7,y:0.5)]
    func testFrameValidationRejectsMalformedContactsAndPhases() throws {
        try frame("begin",pair).validate()
        try frame("end",[]).validate()
        XCTAssertThrowsError(try frame("change",[pair[0],pair[0]]).validate())
        XCTAssertThrowsError(try frame("begin",[.init(id:1,x:.nan,y:0),pair[1]]).validate())
        XCTAssertThrowsError(try frame("cancel",pair).validate())
        XCTAssertThrowsError(try frame("unknown",pair).validate())
        XCTAssertThrowsError(try frame("begin",Array(0..<6).map{ .init(id:$0,x:0,y:0) }).validate())
    }
    func testOptInOwnershipAndCancellationRequireFreshStroke() {
        var idle=TouchpadSender()
        XCTAssertNil(idle.cancel())
        XCTAssertEqual(idle.sample(pair,allowed:true)?.phase,"begin") // Idle handoff must not swallow the first gesture.
        var sender=TouchpadSender()
        XCTAssertNil(sender.sample(pair,allowed:false))
        XCTAssertNil(sender.sample(pair,allowed:true)) // Cannot steal held contacts.
        XCTAssertNil(sender.sample([],allowed:true))
        XCTAssertEqual(sender.sample(pair,allowed:true)?.phase,"begin")
        XCTAssertEqual(sender.sample(pair,allowed:true)?.phase,"change")
        XCTAssertEqual(sender.cancel()?.phase,"cancel")
        XCTAssertNil(sender.cancel())
        XCTAssertNil(sender.sample(pair,allowed:true))
        XCTAssertNil(sender.sample([],allowed:true))
        XCTAssertEqual(sender.sample(pair,allowed:true)?.phase,"begin")
        XCTAssertEqual(sender.sample([pair[0]],allowed:true)?.phase,"end")
        XCTAssertNil(sender.sample(pair,allowed:true))
    }
    func testContactGeometryProducesNativeContinuousGestures() {
        var recognizer=TouchpadRecognizer()
        XCTAssertTrue(recognizer.receive(frame("change",pair)).isEmpty)
        XCTAssertTrue(recognizer.receive(frame("begin",pair)).isEmpty)
        let expanded=[TouchpadContact(id:1,x:0.25,y:0.5),TouchpadContact(id:2,x:0.75,y:0.5)]
        let zoom=recognizer.receive(frame("change",expanded))
        XCTAssertEqual(zoom.map(\.phase),["begin","change"])
        XCTAssertEqual(zoom.last?.kind,.magnify)
        XCTAssertEqual(zoom.last!.value,0.25,accuracy:0.00001)
        XCTAssertEqual(recognizer.receive(frame("end",[])).first?.phase,"end")
        XCTAssertTrue(recognizer.receive(frame("change",expanded)).isEmpty)
        _=recognizer.receive(frame("begin",pair))
        let pan=recognizer.receive(frame("change",pair.map{ .init(id:$0.id,x:$0.x+0.02,y:$0.y) }))
        XCTAssertEqual(pan.last?.kind,.pan)
        XCTAssertEqual(pan.last!.dx,0.02,accuracy:0.00001)
        XCTAssertEqual(recognizer.reset().first?.phase,"cancel")
        let three=pair+[TouchpadContact(id:3,x:0.5,y:0.7)]
        _=recognizer.receive(frame("begin",three))
        let dock=recognizer.receive(frame("change",three.map{ .init(id:$0.id,x:$0.x-0.1,y:$0.y) }))
        XCTAssertEqual(dock.last?.kind,.dockHorizontal)
        XCTAssertEqual(dock.last!.value,0.1,accuracy:0.00001)
    }
    func testNativeGestureConstructionNeverPostsKeysOrPointerMovement() throws {
        let point=CGPoint(x:123,y:456)
        let zoom=try XCTUnwrap(NativeTouchpad.event(.init(kind:.magnify,phase:"change",value:0.125),at:point))
        let cocoa=try XCTUnwrap(NSEvent(cgEvent:zoom))
        XCTAssertEqual(cocoa.type,.magnify)
        XCTAssertEqual(cocoa.phase,.changed)
        XCTAssertEqual(cocoa.magnification,0.125,accuracy:0.00001)
        XCTAssertEqual(zoom.getIntegerValueField(.eventSourceUserData),InputEngine.marker)
        let rotate=try XCTUnwrap(NativeTouchpad.event(.init(kind:.rotate,phase:"change",value:15),at:point))
        XCTAssertEqual(NSEvent(cgEvent:rotate)?.type,.rotate)
        XCTAssertEqual(NSEvent(cgEvent:rotate)!.rotation,15,accuracy:0.0001)
        let pan=try XCTUnwrap(NativeTouchpad.event(.init(kind:.pan,phase:"end"),at:point))
        XCTAssertEqual(pan.type,.scrollWheel)
        XCTAssertEqual(NSEvent(cgEvent:pan)?.phase,.ended)
        XCTAssertEqual(pan.getIntegerValueField(.scrollWheelEventScrollPhase),4)
        let dock=try XCTUnwrap(NativeTouchpad.event(.init(kind:.dockHorizontal,phase:"change",value:0.25),at:point,augmentDock:false))
        XCTAssertEqual(dock.getIntegerValueField(CGEventField(rawValue:55)!),30)
        XCTAssertEqual(dock.getIntegerValueField(CGEventField(rawValue:110)!),23)
        XCTAssertEqual(dock.getDoubleValueField(CGEventField(rawValue:124)!),0.25,accuracy:0.0001)
    }
    func testGesturePayloadUsesTheRunningOSDigitizerLayout() throws {
        let payload=try XCTUnwrap(TouchpadEventPayload.digitizer(timestamp:123456))
        func u32(_ offset:Int)->UInt32 { payload[offset..<offset+4].enumerated().reduce(0){$0 | UInt32($1.element)<<($1.offset*8)} }
        XCTAssertGreaterThan(payload.count,68)
        XCTAssertEqual(u32(24),2) // Hand collection followed by vendor token.
        XCTAssertEqual(u32(32),11)
        let vendor=28+Int(u32(28))
        XCTAssertLessThan(vendor+8,payload.count)
        XCTAssertEqual(u32(vendor+4),1)
        let event=try XCTUnwrap(NativeTouchpad.event(.init(kind:.magnify,phase:"begin"),at:.zero))
        let bytes=try XCTUnwrap(event.data as Data?)
        XCTAssertNotNil(bytes.range(of:Data([0x10,0x6d])))
        XCTAssertEqual(event.getIntegerValueField(.eventSourceUserData),InputEngine.marker)
        XCTAssertEqual(NSEvent(cgEvent:event)?.phase,.began)
    }
    func testModernDockSequenceUsesReversedDirectionAxisAndCompanion() throws {
        let modern=try XCTUnwrap(NativeTouchpad.events(.init(kind:.dockHorizontal,phase:"end",value:0.3,velocity:60),at:.zero,augmentDock:true))
        XCTAssertEqual(modern.count,2)
        let event=modern[0]
        XCTAssertEqual(event.getDoubleValueField(CGEventField(rawValue:124)!),-0.3,accuracy:0.0001)
        XCTAssertEqual(event.getDoubleValueField(CGEventField(rawValue:129)!),-60)
        XCTAssertEqual(event.getDoubleValueField(CGEventField(rawValue:130)!),0)
        XCTAssertEqual(event.getDoubleValueField(CGEventField(rawValue:125)!),0.1,accuracy:0.0001)
        XCTAssertEqual(modern[1].type.rawValue,29)
        XCTAssertTrue(modern.allSatisfy{$0.getIntegerValueField(.eventSourceUserData)==InputEngine.marker})
        let legacy=try XCTUnwrap(NativeTouchpad.events(.init(kind:.dockHorizontal,phase:"change",value:0.3,velocity:60),at:.zero,augmentDock:false))
        XCTAssertEqual(legacy.count,1)
        XCTAssertEqual(legacy[0].getDoubleValueField(CGEventField(rawValue:124)!),0.3,accuracy:0.0001)
        let vertical=try XCTUnwrap(NativeTouchpad.events(.init(kind:.dockVertical,phase:"end",value:0.3,velocity:60),at:.zero,augmentDock:true))
        XCTAssertEqual(vertical[0].getDoubleValueField(CGEventField(rawValue:129)!),0)
        XCTAssertEqual(vertical[0].getDoubleValueField(CGEventField(rawValue:130)!),60)
    }
    func testDockPayloadPhasesProgressAndFlickRelease() throws {
        let bytes=TouchpadEventPayload.dock(phase:4,motion:2,progress:-0.25,velocity:-50,timestamp:123)
        func u32(_ offset:Int)->UInt32 { bytes[offset..<offset+4].enumerated().reduce(0){$0 | UInt32($1.element)<<($1.offset*8)} }
        XCTAssertEqual(bytes.count,96)
        XCTAssertEqual(u32(24),2)
        XCTAssertEqual(u32(36),4<<24)
        XCTAssertEqual(Int32(bitPattern:u32(64)),-16384)
        XCTAssertEqual(u32(72),9)
        let dock=try XCTUnwrap(NativeTouchpad.event(.init(kind:.dockHorizontal,phase:"change",value:-0.25),at:.zero,augmentDock:true))
        XCTAssertEqual(dock.getIntegerValueField(CGEventField(rawValue:134)!),2)
        XCTAssertEqual(dock.getDoubleValueField(CGEventField(rawValue:138)!),3)
        XCTAssertEqual(UInt32(truncatingIfNeeded:dock.getIntegerValueField(CGEventField(rawValue:135)!)),Float(0.25).bitPattern)
        XCTAssertEqual(dock.getIntegerValueField(.eventSourceUserData),InputEngine.marker)
        var r=TouchpadRecognizer()
        let three=pair+[TouchpadContact(id:3,x:0.5,y:0.7)]
        _=r.receive(frame("begin",three),at:1)
        _=r.receive(frame("change",three.map{.init(id:$0.id,x:$0.x-0.1,y:$0.y)}),at:1.02)
        XCTAssertGreaterThan(r.receive(frame("end",[]),at:1.03)[0].velocity,0)
        _=r.receive(frame("begin",three),at:2)
        _=r.receive(frame("change",three.map{.init(id:$0.id,x:$0.x-0.1,y:$0.y)}),at:2.02)
        XCTAssertEqual(r.receive(frame("cancel",[]),at:2.03)[0].velocity,0)
    }
}
