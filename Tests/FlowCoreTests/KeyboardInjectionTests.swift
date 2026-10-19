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
        let generated=CGEventFlags(rawValue:0x20000000).union([.maskShift,.maskAlternate,.maskSecondaryFn])
        let result=KeyboardEventFactory.applyingModifiers([.maskAlphaShift,.maskCommand],to:generated)
        XCTAssertTrue(result.contains([.maskAlphaShift,.maskCommand,.maskSecondaryFn]))
        XCTAssertEqual(result.rawValue & 0x20000000,0x20000000)
        XCTAssertFalse(result.contains(.maskShift))
        XCTAssertFalse(result.contains(.maskAlternate))
        let source=CGEventSource(stateID:.privateState)!
        let original=CGEvent(keyboardEventSource:source,virtualKey:0,keyDown:true)!
        let event=KeyboardEventFactory.make(code:0,down:true,repeated:true,flags:[.maskAlphaShift],source:source)!
        let managed:CGEventFlags=[.maskAlphaShift,.maskShift,.maskControl,.maskAlternate,.maskCommand]
        XCTAssertEqual(event.flags.subtracting(managed),original.flags.subtracting(managed))
        XCTAssertTrue(event.flags.contains(.maskAlphaShift))
        XCTAssertEqual(event.getIntegerValueField(.keyboardEventAutorepeat),1)
    }
    func testCapsIncludesSystemPressAndReleaseWithoutChangingModifierFlags() {
        let press = FlowHIDMakeCapsNotification(true)
        let release = FlowHIDMakeCapsNotification(false)
        for event in [press, release] {
            XCTAssertEqual(event.type, 14) // NX_SYSDEFINED
            XCTAssertEqual(event.data.compound.subType, 8)
            XCTAssertEqual(event.flags, 0)
            XCTAssertEqual(event.options, 0) // inherit the lock state, never toggle it again
        }
        // Caps' special-key ID is 4, distinct from its ADB virtual keycode 57.
        XCTAssertEqual(press.data.compound.misc.L.0, 0x040A00)
        XCTAssertEqual(release.data.compound.misc.L.0, 0x040B00)
    }
    func testOrdinaryKeysInheritDriverModifiersWithoutTextPayload() {
        // Simulate the construction of pinyin, Caps on, uppercase and Caps off.
        // This test constructs structs only; it never opens a driver or posts input.
        let pinyin = FlowHIDMakeKey(0, true, false, false, 0)
        let caps = FlowHIDMakeKey(57, true, true, false, 0x10000)
        let letter = FlowHIDMakeKey(11, true, false, false, 0x10000)
        let capsOff = FlowHIDMakeKey(57, false, true, false, 0)
        XCTAssertEqual(pinyin.type, 10) // NX_KEYDOWN
        XCTAssertEqual(caps.type, 12) // NX_FLAGSCHANGED
        XCTAssertEqual(caps.data.key.keyCode, 57)
        XCTAssertEqual(caps.flags, 0x10000)
        XCTAssertEqual(caps.options, 1) // update driver-global flags before the next key
        XCTAssertEqual(capsOff.flags, 0)
        XCTAssertEqual(letter.data.key.keyCode, 11)
        XCTAssertEqual(letter.type, 10)
        XCTAssertEqual(letter.options, 0) // regular keys inherit the existing driver flags
        XCTAssertEqual(letter.flags, 0)
        for event in [pinyin,caps,letter,capsOff] {
            XCTAssertEqual(event.data.key.charCode, 0)
            XCTAssertEqual(event.data.key.origCharCode, 0)
        }
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
