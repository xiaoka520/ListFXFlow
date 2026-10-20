import XCTest
import AppKit
import CryptoKit
@testable import FlowCore
@testable import FlowMac

final class ClipboardImageTests: XCTestCase {
    private func bitmap() -> NSBitmapImageRep {
        let bitmap=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:8,pixelsHigh:6,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
        for y in 0..<6 { for x in 0..<8 { bitmap.setColor(NSColor(deviceRed:CGFloat(x)/8,green:CGFloat(y)/6,blue:0.4,alpha:0.5),atX:x,y:y) } }
        return bitmap
    }
    func testScreenshotPNGAndTIFFAreReadWithoutAFileURL() throws {
        let board=NSPasteboard.withUniqueName(); defer { board.releaseGlobally() }
        for type in [NSPasteboard.PasteboardType.png,.tiff] {
            let bytes=bitmap().representation(using:type == .png ? .png : .tiff,properties:[:])!
            board.clearContents(); board.setData(bytes,forType:type); board.setString("image description",forType:.string)
            guard case .image(let captured)=ClipboardPasteboard.read(board) else { return XCTFail("Screenshot was ignored") }
            let png=try ClipboardPasteboard.pngData(from:captured), decoded=try XCTUnwrap(NSBitmapImageRep(data:png))
            XCTAssertEqual(decoded.pixelsWide,8); XCTAssertEqual(decoded.pixelsHigh,6)
            XCTAssertEqual(decoded.colorAt(x:4,y:3)!.alphaComponent,0.5,accuracy:0.02)
            if type == .png { XCTAssertEqual(png,bytes,"An existing PNG retains its exact bytes and metadata") }
        }
    }
    func testReceivedImageIsPublishedInBothNativeMacFormats() throws {
        let board=NSPasteboard.withUniqueName(); defer { board.releaseGlobally() }
        let png=bitmap().representation(using:.png,properties:[:])!
        let file=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString+".png"); defer { try? FileManager.default.removeItem(at:file) }
        try png.write(to:file)
        let content=try ClipboardPasteboard.prepare(.image(file))
        XCTAssertTrue(ClipboardPasteboard.write(content,to:board))
        XCTAssertEqual(board.data(forType:.png),png)
        XCTAssertEqual(NSBitmapImageRep(data:board.data(forType:.tiff)!)?.pixelsWide,8)
        XCTAssertTrue(board.canReadObject(forClasses:[NSImage.self],options:nil))
        XCTAssertNil(board.string(forType:.string))
    }
    func testFileCopyRetainsFileSemanticsAndTextStillWorks() {
        let board=NSPasteboard.withUniqueName(); defer { board.releaseGlobally() }
        let file=URL(fileURLWithPath:"/tmp/截图.png")
        XCTAssertTrue(ClipboardPasteboard.write(.files([file]),to:board))
        board.setData(bitmap().representation(using:.png,properties:[:])!,forType:.png)
        guard case .files(let urls)=ClipboardPasteboard.read(board) else { return XCTFail("Copied file became a bitmap") }
        XCTAssertEqual(urls,[file])
        XCTAssertTrue(ClipboardPasteboard.write(.text("中文 🌏"),to:board))
        guard case .text("中文 🌏")=ClipboardPasteboard.read(board) else { return XCTFail() }
    }
    func testInvalidImageNeverClearsExistingClipboard() throws {
        let board=NSPasteboard.withUniqueName(); defer { board.releaseGlobally() }
        board.setString("keep",forType:.string)
        XCTAssertThrowsError(try ClipboardPasteboard.pngData(from:Data("invalid".utf8)))
        XCTAssertEqual(board.string(forType:.string),"keep")
    }
    func testImageTransferNeedsOneHashedFileAndExactCommit() throws {
        let cache=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString); defer { try? FileManager.default.removeItem(at:cache) }
        let inbox=ClipboardInbox(cache:cache), id=UUID().uuidString, bytes=bitmap().representation(using:.png,properties:[:])!
        var packet=ClipboardPacket("begin",id:id); packet.roots=["Clipboard.png"]; packet.count=1; packet.total=Int64(bytes.count); packet.imageFormat="png"
        XCTAssertNil(try inbox.receive(packet))
        packet=ClipboardPacket("entry",id:id); packet.path="Clipboard.png"; packet.directory=false; packet.size=Int64(bytes.count); XCTAssertNil(try inbox.receive(packet))
        XCTAssertThrowsError(try inbox.receive(ClipboardPacket("commit",id:id)))
        packet=ClipboardPacket("data",id:id); packet.bytes=bytes; XCTAssertNil(try inbox.receive(packet))
        packet=ClipboardPacket("fileEnd",id:id); packet.hash=Data(SHA256.hash(data:bytes)).hex; XCTAssertNil(try inbox.receive(packet))
        guard case .image(let url)=try inbox.receive(ClipboardPacket("commit",id:id)) else { return XCTFail("Image became a file-only clipboard") }
        XCTAssertEqual(try Data(contentsOf:url),bytes)
        inbox.abort(); XCTAssertTrue(FileManager.default.fileExists(atPath:url.path))
    }
    func testRejectsInvalidImageManifestAndDirectories() throws {
        var packet=ClipboardPacket("begin",id:UUID().uuidString); packet.roots=["Clipboard.png"]; packet.count=1; packet.total=100; packet.imageFormat="jpeg"
        XCTAssertThrowsError(try packet.validate()); packet.imageFormat="png"
        packet.total=134_217_729; XCTAssertThrowsError(try packet.validate())
        packet.total=100; packet.count=2; XCTAssertThrowsError(try packet.validate())
        packet.count=1; packet.total=0; XCTAssertThrowsError(try packet.validate())
        packet.total=100
        let cache=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString); defer { try? FileManager.default.removeItem(at:cache) }
        let inbox=ClipboardInbox(cache:cache); _=try inbox.receive(packet)
        var entry=ClipboardPacket("entry",id:packet.id); entry.path="Clipboard.png"; entry.directory=true; entry.size=0
        XCTAssertThrowsError(try inbox.receive(entry))
    }
}
