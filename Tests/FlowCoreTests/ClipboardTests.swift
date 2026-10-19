import XCTest
import CryptoKit
@testable import FlowCore

final class ClipboardTests: XCTestCase {
    func testMacScrollIsNotMultipliedByLineCount() {
        XCTAssertEqual(ScrollUnits.fromMac(lines:3,pixels:0,continuous:false),40)
        XCTAssertEqual(ScrollUnits.fromMac(lines:-1.5,pixels:0,continuous:false),-20)
        XCTAssertEqual(ScrollUnits.fromMac(lines:3,pixels:2,continuous:true),2)
        XCTAssertEqual(ScrollUnits.fromMac(lines:0.4,pixels:0,continuous:true),0.4,accuracy:0.000001)
    }
    func testScrollCarryKeepsSubPixelMotion() {
        var scroll = ScrollAccumulator()
        var x = 0, y = 0
        for _ in 0..<5 { scroll.add(dx:0.4,dy:-0.4); let whole=scroll.takeWhole(); x += whole.dx; y += whole.dy }
        XCTAssertEqual(x,2); XCTAssertEqual(y,-2)
        XCTAssertEqual(scroll.dx,0,accuracy:0.000001); XCTAssertEqual(scroll.dy,0,accuracy:0.000001)
        scroll.add(dx:120,dy:-80)
        let burst=scroll.takeWhole()
        XCTAssertEqual(burst.dx,120); XCTAssertEqual(burst.dy,-80)
        XCTAssertFalse(scroll.hasPending)
    }
    func testLayoutPreservesFocusAndScreenPosition() {
        for device in ["mac","windows"] { for side in Side.allCases {
            let router=PointerRouter(); var pointer=Message("pointer"); pointer.active=device; pointer.x=0.23; pointer.y=0.71; router.accept(pointer)
            var next=Topology(); next.side=side; next.offset=420; next.macAspect=2; next.windowsAspect=1.3; router.updateTopology(next)
            let r=next.rect(device)
            XCTAssertEqual(router.active,device)
            XCTAssertEqual((router.position.x-r.x)/r.width,0.23,accuracy: 0.000001)
            XCTAssertEqual((router.position.y-r.y)/r.height,0.71,accuracy: 0.000001)
        } }
    }
    func testRejectsUnsafeAndNonPortablePaths() {
        for name in ["../escape","/absolute","C:/bad","a\\b","a//b","x/../b","NUL.txt","COM1","a.","a ","a\u{0000}b"] { XCTAssertThrowsError(try ClipboardPath.validate(name),name) }
        XCTAssertNoThrow(try ClipboardPath.validate("资料/子目录/文件.txt"))
    }
    func testCompleteFilesAppearOnlyAfterHashesAndCommit() throws {
        let cache=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString); defer { try? FileManager.default.removeItem(at: cache) }
        let inbox=ClipboardInbox(cache: cache), id=UUID().uuidString, bytes=Data("Unicode 内容 🌏".utf8)
        var p=ClipboardPacket("begin",id:id); p.roots=["资料"]; p.count=4; p.total=Int64(bytes.count); XCTAssertNil(try inbox.receive(p))
        p=ClipboardPacket("entry",id:id); p.path="资料"; p.directory=true; p.size=0; XCTAssertNil(try inbox.receive(p))
        p.path="资料/空目录"; XCTAssertNil(try inbox.receive(p))
        p.path="资料/空文件"; p.directory=false; XCTAssertNil(try inbox.receive(p))
        p=ClipboardPacket("fileEnd",id:id); p.hash=Data(SHA256.hash(data:Data())).hex; XCTAssertNil(try inbox.receive(p))
        p=ClipboardPacket("entry",id:id); p.path="资料/文件.txt"; p.directory=false; p.size=Int64(bytes.count); XCTAssertNil(try inbox.receive(p))
        p=ClipboardPacket("data",id:id); p.bytes=bytes; XCTAssertNil(try inbox.receive(p))
        p=ClipboardPacket("fileEnd",id:id); p.hash=Data(SHA256.hash(data:bytes)).hex; XCTAssertNil(try inbox.receive(p))
        guard case .files(let urls)=try inbox.receive(ClipboardPacket("commit",id:id)) else { return XCTFail("No completed file list") }
        XCTAssertEqual(try Data(contentsOf:urls[0].appendingPathComponent("文件.txt")),bytes)
        XCTAssertTrue(FileManager.default.fileExists(atPath:urls[0].appendingPathComponent("空目录").path))
        inbox.abort(); XCTAssertTrue(FileManager.default.fileExists(atPath:urls[0].path),"Committed clipboard data survives disconnection")
    }
    func testPartialAndCorruptTransfersNeverCommit() throws {
        let cache=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString); defer { try? FileManager.default.removeItem(at: cache) }
        let inbox=ClipboardInbox(cache:cache), id=UUID().uuidString
        var p=ClipboardPacket("begin",id:id); p.roots=["x"]; p.count=1; p.total=1; _=try inbox.receive(p)
        p=ClipboardPacket("entry",id:id); p.path="x"; p.size=1; p.directory=false; _=try inbox.receive(p)
        XCTAssertThrowsError(try inbox.receive(ClipboardPacket("commit",id:id)))
        p=ClipboardPacket("data",id:id); p.bytes=Data([7]); _=try inbox.receive(p)
        p=ClipboardPacket("fileEnd",id:id); p.hash=String(repeating:"0",count:64); XCTAssertThrowsError(try inbox.receive(p))
        inbox.abort(); XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:cache.path),[])
    }
    func testDuplicatePathsAndNewCopyCancelOldTransfer() throws {
        let cache=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString); defer { try? FileManager.default.removeItem(at: cache) }
        let inbox=ClipboardInbox(cache:cache), id=UUID().uuidString
        var p=ClipboardPacket("begin",id:id); p.roots=["dir"]; p.count=2; p.total=0; _=try inbox.receive(p)
        p=ClipboardPacket("entry",id:id); p.path="dir"; p.directory=true; p.size=0; _=try inbox.receive(p)
        XCTAssertThrowsError(try inbox.receive(p))
        p=ClipboardPacket("text",id:UUID().uuidString); p.text="new copy"
        guard case .text("new copy")=try inbox.receive(p) else { return XCTFail() }
        XCTAssertNil(inbox.id); XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath:cache.path),[])
    }
    func testManifestRejectsSymlinksAndCountsEmptyFolders() throws {
        let root=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString); defer { try? FileManager.default.removeItem(at:root) }
        try FileManager.default.createDirectory(at:root.appendingPathComponent("empty"),withIntermediateDirectories:true)
        let manifest=try ClipboardManifest(urls:[root]); XCTAssertEqual(manifest.entries.map(\.path),[root.lastPathComponent,root.lastPathComponent+"/empty"]); XCTAssertEqual(manifest.entries.count,2); XCTAssertEqual(manifest.total,0)
        try FileManager.default.createSymbolicLink(at:root.appendingPathComponent("link"),withDestinationURL:root)
        XCTAssertThrowsError(try ClipboardManifest(urls:[root]))
    }
    func testBinaryClipboardEncryptionAndPacketLimits() throws {
        let key=Data.random(32),c=Data.random(32),s=Data.random(32)
        let a=SecureChannel(secret:key,client:c,server:s,isServer:true),b=SecureChannel(secret:key,client:c,server:s,isServer:false)
        var packet=ClipboardPacket("data",id:UUID().uuidString); packet.bytes=Data.random(196608)
        let encoded=try JSONEncoder().encode(packet),decoded=try JSONDecoder().decode(ClipboardPacket.self,from:b.openData(a.sealData(encoded)))
        XCTAssertEqual(decoded.bytes,packet.bytes)
        packet.bytes=Data(repeating:0,count:196609); XCTAssertThrowsError(try packet.validate())
    }
}

final class ScrollIdleTests: XCTestCase {
    func testFractionalCarrySurvivesIdleWithoutKeepingTheFrameTimerAlive() {
        var scroll = ScrollAccumulator()
        scroll.add(dx: 0.25,dy: -0.25)
        XCTAssertFalse(scroll.hasPending)
        XCTAssertEqual(scroll.takeWhole().dx,0)
        scroll.add(dx: 0.75,dy: -0.75)
        XCTAssertTrue(scroll.hasPending)
        let delta = scroll.takeWhole()
        XCTAssertEqual(delta.dx,1)
        XCTAssertEqual(delta.dy,-1)
        XCTAssertFalse(scroll.hasPending)
    }
}
