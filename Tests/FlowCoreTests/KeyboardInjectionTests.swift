import XCTest
import FlowCore
import FlowHID

final class KeyboardInjectionTests: XCTestCase {
    func testRejectedPreLoginMouseEventsNeverPostInput() {
        var buttons:UInt8=3
        XCTAssertNotEqual(FlowSessionPostMouse(nil,&buttons),0)
        let key=CGEvent(keyboardEventSource:nil,virtualKey:0,keyDown:true)!
        XCTAssertNotEqual(FlowSessionPostMouse(key,&buttons),0)
        XCTAssertEqual(buttons,3)
    }
    func testRemoteModifiersDoNotEraseQuartzKeyboardFlags() {
        let generated=CGEventFlags(rawValue:0x20000000).union([.maskAlphaShift,.maskShift,.maskAlternate,.maskSecondaryFn])
        let result=KeyboardEventFactory.applyingModifiers([.maskCommand],to:generated)
        XCTAssertTrue(result.contains([.maskAlphaShift,.maskCommand,.maskSecondaryFn]))
        XCTAssertEqual(result.rawValue & 0x20000000,0x20000000)
        XCTAssertFalse(result.contains(.maskShift))
        XCTAssertFalse(result.contains(.maskAlternate))
        let source=CGEventSource(stateID:.privateState)!
        let original=CGEvent(keyboardEventSource:source,virtualKey:0,keyDown:true)!
        let event=KeyboardEventFactory.make(code:0,down:true,repeated:true,flags:[.maskCommand],source:source)!
        let managed:CGEventFlags=[.maskShift,.maskControl,.maskAlternate,.maskCommand]
        XCTAssertEqual(event.flags.subtracting(managed),original.flags.subtracting(managed))
        XCTAssertFalse(KeyboardEventFactory.applyingModifiers([.maskAlphaShift],to:[]).contains(.maskAlphaShift))
        XCTAssertEqual(event.getIntegerValueField(.keyboardEventAutorepeat),1)
    }
    func testRemoteCapsIsPhysicalInputWithoutARequestedLockState() {
        let press = FlowHIDMakePhysicalCaps(true)
        let release = FlowHIDMakePhysicalCaps(false)
        for event in [press, release] {
            XCTAssertEqual(event.data.key.keyCode, 57)
            XCTAssertEqual(event.data.key.repeat, 0)
            XCTAssertEqual(event.flags, 0)
            XCTAssertEqual(event.options, 8) // HID manager input, without setting global flags
        }
        XCTAssertEqual(press.type, 10) // NX_KEYDOWN
        XCTAssertEqual(release.type, 11) // NX_KEYUP
    }
    func testOrdinaryKeysInheritDriverModifiersWithoutTextPayload() {
        // This test constructs structs only; it never opens a driver or posts input.
        let letter = FlowHIDMakeKey(11, true, false, false, 0x10000)
        XCTAssertEqual(letter.data.key.keyCode, 11)
        XCTAssertEqual(letter.type, 10)
        XCTAssertEqual(letter.options, 0) // regular keys inherit the existing driver flags
        XCTAssertEqual(letter.flags, 0)
        XCTAssertEqual(letter.data.key.charCode, 0)
        XCTAssertEqual(letter.data.key.origCharCode, 0)
    }
    func testModifiersReleasesAndAutoRepeatKeepPhysicalSemantics() {
        let shift = FlowHIDMakeKey(56,true,true,false,0x20000)
        XCTAssertEqual(shift.type,12)
        XCTAssertEqual(shift.data.key.keyCode,56)
        XCTAssertEqual(shift.flags,0x20000)
        let repeated = FlowHIDMakeKey(0,true,false,true,0)
        XCTAssertEqual(repeated.data.key.repeat,1)
        let release = FlowHIDMakeKey(0,false,false,false,0)
        XCTAssertEqual(release.type,11) // NX_KEYUP
        XCTAssertEqual(release.data.key.keyCode,0)
    }
    func testDriverEchoCannotBecomeAForwardedKeyAfterHandoff() {
        var echo = KeyboardEcho()
        echo.expect(code:57,type:12,flags:0x10000,repeated:false,now:1)
        echo.expect(code:0,type:10,flags:0x10000,repeated:false,now:1)
        XCTAssertFalse(echo.consume(code:0,type:11,flags:0x10000,repeated:false,now:1.01))
        XCTAssertFalse(echo.consume(code:0,type:10,flags:0,repeated:false,now:1.01))
        XCTAssertTrue(echo.consume(code:57,type:12,flags:0x10000,repeated:false,now:1.01))
        XCTAssertTrue(echo.consume(code:0,type:10,flags:0x10000,repeated:false,now:1.01))
        XCTAssertFalse(echo.consume(code:0,type:10,flags:0x10000,repeated:false,now:1.01))
        echo.expect(code:0,type:10,flags:0,repeated:true,now:2)
        XCTAssertFalse(echo.consume(code:0,type:10,flags:0,repeated:true,now:3))
    }
}
