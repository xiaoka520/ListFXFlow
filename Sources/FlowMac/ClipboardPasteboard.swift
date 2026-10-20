import AppKit
import ImageIO
import UniformTypeIdentifiers
import FlowCore

/// Keep AppKit pasteboard access on the UI thread; encode/decode on the clipboard worker.
enum ClipboardPasteboard {
    enum Snapshot {
        case files([URL]), image(Data), text(String), unsupported
    }
    enum Prepared {
        case files([URL]), image(png: Data,tiff: Data), text(String)
    }
    static func read(_ board: NSPasteboard) -> Snapshot {
        if let urls=board.readObjects(forClasses: [NSURL.self],options: [.urlReadingFileURLsOnly:true]) as? [URL], !urls.isEmpty { return .files(urls) }
        // Screenshots are bitmap data, without a file URL. Prefer the image over an
        // application's accompanying text description or HTML representation.
        for type in [NSPasteboard.PasteboardType.png,.tiff] {
            if let bytes=board.data(forType: type), !bytes.isEmpty { return .image(bytes) }
        }
        if let text=board.string(forType: .string) { return .text(text) }
        return .unsupported
    }
    private static func image(_ bytes: Data) throws -> CGImage {
        guard !bytes.isEmpty, bytes.count <= 134_217_728,
              let source=CGImageSourceCreateWithData(bytes as CFData,nil),
              let info=CGImageSourceCopyPropertiesAtIndex(source,0,nil) as? [CFString:Any],
              let width=info[kCGImagePropertyPixelWidth] as? Int,
              let height=info[kCGImagePropertyPixelHeight] as? Int,
              width > 0, height > 0, width <= 32768, height <= 32768, width*height <= 100_000_000,
              let image=CGImageSourceCreateImageAtIndex(source,0,nil) else { throw ClipboardError.invalid }
        return image
    }
    static func pngData(from bytes: Data) throws -> Data {
        let image=try image(bytes)
        // Retain an existing PNG exactly, including transparency and colour metadata.
        if bytes.starts(with: [137,80,78,71,13,10,26,10]) { return bytes }
        let result=NSMutableData()
        guard let output=CGImageDestinationCreateWithData(result,UTType.png.identifier as CFString,1,nil) else { throw ClipboardError.invalid }
        CGImageDestinationAddImage(output,image,nil)
        guard CGImageDestinationFinalize(output), result.length <= 134_217_728 else { throw ClipboardError.invalid }
        return result as Data
    }
    static func prepare(_ result: ClipboardResult) throws -> Prepared {
        switch result {
        case .text(let text): return .text(text)
        case .files(let urls): return .files(urls)
        case .image(let url):
            let png=try Data(contentsOf: url), bitmap=try image(png)
            guard png.starts(with: [137,80,78,71,13,10,26,10]),
                  let tiff=NSBitmapImageRep(cgImage: bitmap).representation(using: .tiff,properties: [:]) else { throw ClipboardError.invalid }
            return .image(png: png,tiff: tiff)
        }
    }
    @discardableResult static func write(_ content: Prepared,to board: NSPasteboard) -> Bool {
        board.clearContents()
        switch content {
        case .text(let text): return board.setString(text,forType: .string)
        case .files(let urls): return board.writeObjects(urls.map { $0 as NSURL })
        case .image(let png,let tiff):
            let item=NSPasteboardItem()
            guard item.setData(png,forType: .png), item.setData(tiff,forType: .tiff) else { return false }
            return board.writeObjects([item])
        }
    }
}
