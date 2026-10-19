import Foundation

/// IOHID events have no Quartz user-data marker. Recognize only the exact native
/// events just posted, including flags and repeat, so they cannot be forwarded back
/// after a focus handoff. All access is confined to the Mac input queue.
public struct KeyboardEcho {
    private struct Entry { let code: Int; let type: UInt32; let flags: UInt64; let repeated: Bool; let until: Double }
    private var entries: [Entry] = []
    public init() {}
    public mutating func expect(code: Int, type: UInt32, flags: UInt64, repeated: Bool, now: Double) {
        entries.removeAll { $0.until < now }
        if entries.count >= 1024 { entries.removeFirst() }
        entries.append(Entry(code: code, type: type, flags: flags, repeated: repeated, until: now + 0.5))
    }
    public mutating func consume(code: Int, type: UInt32, flags: UInt64, repeated: Bool, now: Double) -> Bool {
        entries.removeAll { $0.until < now }
        guard let index = entries.firstIndex(where: { $0.code == code && $0.type == type && $0.flags == flags && $0.repeated == repeated }) else { return false }
        entries.remove(at: index)
        return true
    }
}
