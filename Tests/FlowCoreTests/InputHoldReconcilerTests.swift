import XCTest
import FlowCore
@testable import FlowMac

final class InputHoldReconcilerTests:XCTestCase {
    func testLostReleaseNoLongerClosesTheMacSeam() {
        for side in Side.allCases {
            let router=PointerRouter();router.topology.side=side
            var keys:Set<Int>=[4,225],buttons:Set<Int>=[0]
            let sample:(Double,Double,Double,Double)
            switch side {
            case .left: sample=(0,500,-8,0)
            case .right: sample=(2559,500,8,0)
            case .top: sample=(1280,0,0,-8)
            case .bottom: sample=(1280,1439,0,8)
            }
            func move()->String? {
                router.move(local:"mac",x:sample.0,y:sample.1,dx:sample.2,dy:sample.3,
                            width:2560,height:1440,mayCross:keys.isEmpty && buttons.isEmpty).active
            }
            XCTAssertEqual(move(),"mac")
            InputHoldReconciler.reconcile(keys:&keys,buttons:&buttons,keyDown:{_ in false},buttonDown:{_ in false})
            XCTAssertEqual(move(),"windows",side.rawValue)
        }
    }
    func testRealHoldsStillBlockAndGlobalInjectedPressesAreNotAdded() {
        var keys:Set<Int>=[4,225],buttons:Set<Int>=[0,1]
        InputHoldReconciler.reconcile(keys:&keys,buttons:&buttons,keyDown:{$0==225 || $0==227},buttonDown:{$0==1 || $0==2})
        XCTAssertEqual(keys,[225]);XCTAssertEqual(buttons,[1])
        InputHoldReconciler.reconcile(keys:&keys,buttons:&buttons,keyDown:{_ in false},buttonDown:{_ in false})
        XCTAssertTrue(keys.isEmpty && buttons.isEmpty)
    }
    func testSourceSwitchUsesTheLatestLocalPositionAndRespectsTheGap() {
        for x in [20.0,800.0] {
            let mac=PointerRouter(),win=PointerRouter()
            var topology=Topology();topology.side = .bottom;topology.offset=95
            mac.updateTopology(topology);win.updateTopology(topology)
            let sync=mac.move(local:"mac",x:x,y:1436,dx:0,dy:1,width:2560,height:1440)
            XCTAssertEqual(sync.active,"mac")
            win.accept(sync)
            XCTAssertEqual(win.position,mac.position)
            let fromMac=mac.move(local:"mac",x:x,y:1439,dx:0,dy:8,width:2560,height:1440)
            let fromWin=win.move(local:"windows",x:0,y:0,dx:0,dy:20,width:2560,height:1440)
            XCTAssertEqual(fromMac.active,x==20 ? "mac":"windows")
            XCTAssertEqual(fromWin.active,fromMac.active)
        }
    }
}
