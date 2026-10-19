import XCTest
import FlowCore

final class PointerParkingTests:XCTestCase {
    func testParkingUsesTheSeamAndKeepsTheAlongEdgePosition() {
        let r=CGRect(x:-1920,y:120,width:1920,height:1080),p=CGPoint(x:-900,y:431)
        XCTAssertEqual(PointerParking.anchor(sample:p,bounds:r,side:.left),CGPoint(x:-1920,y:431))
        XCTAssertEqual(PointerParking.anchor(sample:p,bounds:r,side:.right),CGPoint(x:-1,y:431))
        XCTAssertEqual(PointerParking.anchor(sample:p,bounds:r,side:.top),CGPoint(x:-900,y:120))
        XCTAssertEqual(PointerParking.anchor(sample:p,bounds:r,side:.bottom),CGPoint(x:-900,y:1199))
        XCTAssertEqual(PointerParking.anchor(sample:CGPoint(x:9999,y:-999),bounds:r,side:.right),CGPoint(x:-1,y:120))
    }
}
