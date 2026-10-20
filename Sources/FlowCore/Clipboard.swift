import Foundation
import CryptoKit

public struct ClipboardPacket: Codable {
    public var kind: String
    public var id: String
    public var text: String?
    public var roots: [String]?
    public var count: Int?
    public var total: Int64?
    public var path: String?
    public var size: Int64?
    public var directory: Bool?
    public var bytes: Data?
    public var hash: String?
    public var imageFormat: String?
    public init(_ kind: String, id: String) { self.kind=kind; self.id=id }
    public func validate() throws {
        guard UUID(uuidString: id) != nil else { throw ClipboardError.invalid }
        switch kind {
        case "text": guard let text, text.utf8.count <= 524288 else { throw ClipboardError.invalid }
        case "begin":
            guard let roots, !roots.isEmpty, roots.count <= 256, let count, (roots.count...100000).contains(count), let total, (0...1_099_511_627_776).contains(total) else { throw ClipboardError.invalid }
            guard imageFormat == nil || (imageFormat == "png" && roots.count == 1 && count == 1 && total > 0 && total <= 134_217_728) else { throw ClipboardError.invalid }
            var names=Set<String>()
            for root in roots { try ClipboardPath.validate(root); guard !root.contains("/"), names.insert(root.lowercased()).inserted else { throw ClipboardError.invalid } }
        case "entry": guard let path, let size, size >= 0, size <= 1_099_511_627_776, let directory, !directory || size == 0 else { throw ClipboardError.invalid }; try ClipboardPath.validate(path)
        case "data": guard let bytes, !bytes.isEmpty, bytes.count <= 196608 else { throw ClipboardError.invalid }
        case "fileEnd": guard let hash, hash.count == 64, Data(hex: hash)?.count == 32 else { throw ClipboardError.invalid }
        case "commit", "cancel", "done": break
        default: throw ClipboardError.invalid
        }
    }
}
public enum ClipboardError: LocalizedError {
    case invalid, incomplete, changed, unsupported(String), space
    public var errorDescription: String? {
        switch self {
        case .invalid: return "剪贴板数据或文件名不受支持"
        case .incomplete: return "文件传输未完成或校验失败"
        case .changed: return "源文件在复制期间发生变化，请重新复制"
        case .unsupported(let name): return "无法同步此文件（特殊文件、链接或名称冲突）：\(name)"
        case .space: return "接收端磁盘空间不足"
        }
    }
}
public enum ClipboardPath {
    public static func validate(_ path: String) throws {
        guard path.utf8.count <= 4096, path == path.precomposedStringWithCanonicalMapping else { throw ClipboardError.invalid }
        let parts=path.split(separator: "/", omittingEmptySubsequences: false)
        guard !parts.isEmpty, parts.count <= 64 else { throw ClipboardError.invalid }
        let reserved=Set(["CON","PRN","AUX","NUL"] + (1...9).flatMap { ["COM\($0)","LPT\($0)"] })
        for part in parts {
            let name=String(part)
            guard !name.isEmpty, name != ".", name != "..", name.utf8.count <= 255,
                  !name.hasSuffix("."), !name.hasSuffix(" "),
                  !name.unicodeScalars.contains(where: { $0.value < 32 || "\\:<>\"|?*".unicodeScalars.contains($0) }),
                  !reserved.contains(String(name.split(separator: ".")[0]).uppercased()) else { throw ClipboardError.invalid }
        }
    }
}
public struct ClipboardEntry {
    public let url: URL
    public let path: String
    public let size: Int64
    public let directory: Bool
}
public struct ClipboardManifest {
    public let roots: [String]
    public let entries: [ClipboardEntry]
    public let total: Int64
    public init(urls: [URL]) throws {
        guard !urls.isEmpty, urls.count <= 256 else { throw ClipboardError.invalid }
        var entries=[ClipboardEntry](), roots=[String](), seen=Set<String>(), total: Int64=0
        let keys: Set<URLResourceKey>=[.isRegularFileKey,.isDirectoryKey,.isSymbolicLinkKey,.fileSizeKey]
        func add(_ url: URL, _ path: String) throws {
            try ClipboardPath.validate(path)
            guard seen.insert(path.lowercased()).inserted, entries.count < 100000 else { throw ClipboardError.unsupported(path) }
            let info=try url.resourceValues(forKeys: keys)
            guard info.isSymbolicLink != true, info.isDirectory == true || info.isRegularFile == true else { throw ClipboardError.unsupported(path) }
            let directory=info.isDirectory == true, size=directory ? 0 : Int64(info.fileSize ?? 0)
            guard size >= 0, total <= 1_099_511_627_776-size else { throw ClipboardError.invalid }
            total += size; entries.append(ClipboardEntry(url: url,path: path,size: size,directory: directory))
        }
        for original in urls {
            guard try original.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true else { throw ClipboardError.unsupported(original.lastPathComponent) }
            let url=original.standardizedFileURL.resolvingSymlinksInPath()
            let name=original.lastPathComponent.precomposedStringWithCanonicalMapping
            roots.append(name); try add(url,name)
            if entries.last!.directory {
                var enumerationError: Error?
                guard let walker=FileManager.default.enumerator(at: url,includingPropertiesForKeys: Array(keys),options: [],errorHandler: { _,error in enumerationError=error; return false }) else { throw ClipboardError.unsupported(name) }
                for case let child as URL in walker {
                    guard try child.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true else { throw ClipboardError.unsupported(child.lastPathComponent) }
                    let base=url.pathComponents, components=child.standardizedFileURL.resolvingSymlinksInPath().pathComponents
                    guard components.count > base.count, Array(components.prefix(base.count)) == base else { throw ClipboardError.unsupported(child.lastPathComponent) }
                    let suffix=components.dropFirst(base.count).joined(separator: "/")
                    try add(child,name+"/"+suffix.precomposedStringWithCanonicalMapping)
                }
                if let enumerationError { throw enumerationError }
            }
        }
        self.roots=roots; self.entries=entries; self.total=total
    }
}
public enum ClipboardResult { case text(String); case files([URL]); case image(URL) }

/// Confined to one clipboard connection's worker. Incomplete files never reach the OS clipboard.
public final class ClipboardInbox {
    public let cache: URL
    public private(set) var id: String?
    public private(set) var received: Int64=0
    public private(set) var total: Int64=0
    private var folder: URL?
    private var roots=[String](), expected=0, entries=Set<String>(), directories=Set<String>()
    private var declared: Int64=0, remaining: Int64=0
    private var file: FileHandle?, digest=SHA256()
    private var imageFormat: String?
    public init(cache: URL) { self.cache=cache }
    public func abort() {
        try? file?.close(); file=nil
        if let folder { try? FileManager.default.removeItem(at: folder) }
        imageFormat=nil; folder=nil; id=nil; roots=[]; entries=[]; directories=[]; remaining=0; declared=0; received=0; total=0
    }
    public func receive(_ packet: ClipboardPacket) throws -> ClipboardResult? {
        try packet.validate()
        if packet.kind == "text" { abort(); return .text(packet.text!) }
        if packet.kind == "begin" {
            abort(); try FileManager.default.createDirectory(at: cache,withIntermediateDirectories: true)
            if let free=(try FileManager.default.attributesOfFileSystem(forPath: cache.path)[.systemFreeSize] as? NSNumber)?.int64Value, free < packet.total!+16_777_216 { throw ClipboardError.space }
            let next=cache.appendingPathComponent(UUID().uuidString,isDirectory: true)
            try FileManager.default.createDirectory(at: next,withIntermediateDirectories: false)
            folder=next; id=packet.id; imageFormat=packet.imageFormat; roots=packet.roots!; expected=packet.count!; total=packet.total!; return nil
        }
        guard id == packet.id, let folder else { return nil }
        switch packet.kind {
        case "entry":
            guard file == nil, entries.count < expected else { throw ClipboardError.invalid }
            let path=packet.path!, canonical=path.lowercased(), parts=path.split(separator: "/")
            guard roots.contains(String(parts[0])), entries.insert(canonical).inserted else { throw ClipboardError.invalid }
            if parts.count > 1 { guard directories.contains(parts.dropLast().joined(separator: "/").lowercased()) else { throw ClipboardError.invalid } }
            let target=folder.appendingPathComponent(path)
            guard !FileManager.default.fileExists(atPath: target.path) else { throw ClipboardError.invalid }
            guard imageFormat == nil || !packet.directory! else { throw ClipboardError.invalid }
            if packet.directory! { try FileManager.default.createDirectory(at: target,withIntermediateDirectories: false); directories.insert(canonical) }
            else {
                guard declared <= total-packet.size! else { throw ClipboardError.invalid }; declared += packet.size!
                try Data().write(to: target,options: .withoutOverwriting)
                file=try FileHandle(forWritingTo: target); remaining=packet.size!; digest=SHA256()
            }
        case "data":
            guard let file, Int64(packet.bytes!.count) <= remaining else { throw ClipboardError.invalid }
            try file.write(contentsOf: packet.bytes!); digest.update(data: packet.bytes!); remaining -= Int64(packet.bytes!.count); received += Int64(packet.bytes!.count)
        case "fileEnd":
            guard let file, remaining == 0, Data(digest.finalize()).hex == packet.hash!.lowercased() else { throw ClipboardError.incomplete }
            try file.close(); self.file=nil
        case "commit":
            guard file == nil, entries.count == expected, received == total, declared == total,
                  roots.allSatisfy({ entries.contains($0.lowercased()) }) else { throw ClipboardError.incomplete }
            let urls=roots.map { folder.appendingPathComponent($0) }; self.folder=nil; id=nil
            return imageFormat == "png" ? .image(urls[0]) : .files(urls)
        case "cancel": abort()
        default: throw ClipboardError.invalid
        }
        return nil
    }
    deinit { abort() }
}
