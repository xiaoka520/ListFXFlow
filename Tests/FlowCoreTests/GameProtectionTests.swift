import XCTest
import FlowCore
@testable import FlowMac

final class GameProtectionTests:XCTestCase {
    func testRemoteMouseCannotCrossAnyLockedSeamAndResetRestoresCrossing() {
        for side in Side.allCases {
            let r=PointerRouter();r.topology.side=side
            let edge:(Double,Double,Double,Double)
            switch side {
            case .left: edge=(0,500,-8,0)
            case .right: edge=(1599,500,8,0)
            case .top: edge=(800,0,0,-8)
            case .bottom: edge=(800,999,0,8)
            }
            _=r.move(local:"mac",x:edge.0,y:edge.1,dx:edge.2,dy:edge.3,width:1600,height:1000)
            r.crossingLocked=true
            XCTAssertEqual(r.move(local:"mac",x:0,y:0,dx:-edge.2*100000,dy:-edge.3*100000,width:1600,height:1000).active,"windows")
            let before=r.position
            var stale=Message("pointer");stale.active="mac";stale.x=0.5;stale.y=0.5
            XCTAssertFalse(r.accept(stale));XCTAssertEqual(r.position,before)
            r.reset(local:"mac");XCTAssertFalse(r.crossingLocked)
            XCTAssertEqual(r.move(local:"mac",x:edge.0,y:edge.1,dx:edge.2,dy:edge.3,width:1600,height:1000).active,"windows")
        }
    }
    func testShortcutRepeatReleaseAndOrdinaryTyping() {
        var key=GameShortcut()
        XCTAssertTrue(key.process(down:true,chord:true,available:true).toggle)
        XCTAssertFalse(key.process(down:true,chord:true,available:true).toggle)
        XCTAssertTrue(key.process(down:false,chord:false,available:false).consume)
        XCTAssertFalse(key.process(down:true,chord:false,available:true).consume)
        XCTAssertFalse(key.process(down:true,chord:true,available:true).toggle)
        _=key.process(down:false,chord:false,available:true)
        XCTAssertFalse(key.process(down:true,chord:true,available:false).consume)
    }
    func testGameModeAndRelativeProtocolValidation() throws {
        var mode=Message("gameMode");mode.down=true;mode.phase="manual";mode.active="windows";mode.x=0.25;mode.y=0.75
        XCTAssertNoThrow(try mode.validate());mode.phase="off";XCTAssertThrowsError(try mode.validate())
        mode.down=false;XCTAssertNoThrow(try mode.validate());mode.active="mac";XCTAssertThrowsError(try mode.validate())
        var relative=Message("relativePointer");relative.dx=123.5;relative.dy = -45
        XCTAssertNoThrow(try relative.validate());relative.dx = .infinity;XCTAssertThrowsError(try relative.validate())
        XCTAssertThrowsError(try Message("gameModeRequest").validate())
    }
}
