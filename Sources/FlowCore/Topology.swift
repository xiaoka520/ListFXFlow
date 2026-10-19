import Foundation

public enum Side: String, CaseIterable, Codable { case left, right, top, bottom }
public struct Point: Equatable { public var x: Double; public var y: Double; public init(_ x: Double, _ y: Double) { self.x=x; self.y=y } }
public struct ScreenRect: Equatable {
    public var x: Double; public var y: Double; public var width: Double; public var height: Double
    public var maxX: Double { x + width }; public var maxY: Double { y + height }
    public func contains(_ p: Point) -> Bool { p.x >= x && p.x < maxX && p.y >= y && p.y < maxY }
    public func clamp(_ p: Point) -> Point { Point(min(maxX - 0.01, max(x, p.x)), min(maxY - 0.01, max(y, p.y))) }
}

/// Both systems share logical desktop coordinates. Each primary display is 1000 units high.
public struct Topology {
    public var side: Side = .right
    public var offset: Double = 0
    public var macAspect: Double = 16.0 / 9
    public var windowsAspect: Double = 16.0 / 10
    public init() {}
    public var mac: ScreenRect { ScreenRect(x: 0, y: 0, width: macAspect * 1000, height: 1000) }
    public var windows: ScreenRect {
        let width = windowsAspect * 1000
        switch side {
        case .left: return ScreenRect(x: -width, y: offset, width: width, height: 1000)
        case .right: return ScreenRect(x: mac.width, y: offset, width: width, height: 1000)
        case .top: return ScreenRect(x: offset, y: -1000, width: width, height: 1000)
        case .bottom: return ScreenRect(x: offset, y: 1000, width: width, height: 1000)
        }
    }
    public func rect(_ device: String) -> ScreenRect { device == "mac" ? mac : windows }
}

public final class PointerRouter {
    public var topology = Topology()
    public private(set) var active = "mac"
    public private(set) var position = Point(500, 500)
    public var crossingLocked = false
    public init() {}
    public func updateTopology(_ value: Topology) {
        let old = topology.rect(active)
        let nx = (position.x-old.x)/old.width, ny = (position.y-old.y)/old.height
        topology = value
        let next = topology.rect(active)
        position = next.clamp(Point(next.x+nx*next.width, next.y+ny*next.height))
    }
    @discardableResult public func accept(_ message: Message) -> Bool {
        guard let device = message.active, let x = message.x, let y = message.y,
              !crossingLocked || device == active else { return false }
        active = device; let rect = topology.rect(device)
        position = rect.clamp(Point(rect.x + x * rect.width, rect.y + y * rect.height))
        return true
    }
    public func reset(local: String) { crossingLocked=false;active = local; let r = topology.rect(local); position = Point(r.x+r.width/2, r.y+r.height/2) }
    /// Return true when local mouse delivery should be suppressed.
    public func move(local: String, x: Double, y: Double, dx: Double, dy: Double, width: Double, height: Double, mayCross: Bool = true) -> Message {
        let r = topology.rect(active)
        var p: Point
        if active == local {
            p = Point(r.x + x / width * r.width, r.y + y / height * r.height)
            // OS coordinates clamp at screen edges, so project one unit beyond an outward edge.
            if x <= 1 && dx < 0 { p.x = r.x - 1 }
            if x >= width - 2 && dx > 0 { p.x = r.maxX + 1 }
            if y <= 1 && dy < 0 { p.y = r.y - 1 }
            if y >= height - 2 && dy > 0 { p.y = r.maxY + 1 }
        } else { p = Point(position.x + dx * 1000 / height, position.y + dy * 1000 / height) }
        let other = active == "mac" ? "windows" : "mac"
        // Test the seam intersection, not just the endpoint: large deltas cannot skip a display.
        if !crossingLocked,mayCross, !r.contains(p), crosses(from: position, to: p, current: r, target: topology.rect(other)) {
            active = other; position = topology.rect(other).clamp(p)
        } else { position = r.clamp(p) }
        let target = topology.rect(active)
        var m = Message("pointer"); m.active = active
        m.x = (position.x - target.x) / target.width; m.y = (position.y - target.y) / target.height
        return m
    }
    private func crosses(from a: Point, to b: Point, current: ScreenRect, target: ScreenRect) -> Bool {
        if target.contains(b) { return true }
        let dx = b.x-a.x, dy = b.y-a.y
        var t: Double?
        if b.x < current.x && dx < 0 { t = (current.x-a.x)/dx }
        else if b.x >= current.maxX && dx > 0 { t = (current.maxX-a.x)/dx }
        else if b.y < current.y && dy < 0 { t = (current.y-a.y)/dy }
        else if b.y >= current.maxY && dy > 0 { t = (current.maxY-a.y)/dy }
        guard let t, (0...1).contains(t) else { return false }
        let seam = Point(a.x+dx*t + (dx == 0 ? 0 : dx/abs(dx)*0.01), a.y+dy*t + (dy == 0 ? 0 : dy/abs(dy)*0.01))
        return target.contains(seam)
    }
}

public struct KeyEntry: Codable { public let hid: Int; public let mac: Int; public let scan: Int; public let extended: Bool }
public enum KeyMap {
    public static let entries: [KeyEntry] = {
        let url = Bundle.module.url(forResource: "keymap", withExtension: "json")!
        return try! JSONDecoder().decode([KeyEntry].self, from: Data(contentsOf: url))
    }()
    public static let byMac = Dictionary(uniqueKeysWithValues: entries.map { ($0.mac, $0.hid) })
    public static let byHID = Dictionary(uniqueKeysWithValues: entries.map { ($0.hid, $0.mac) })
}
