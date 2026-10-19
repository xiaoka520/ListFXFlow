import Foundation

public enum ScrollUnits {
    /// Three macOS lines correspond to one standard wheel detent (40 wire units).
    /// Continuous pixel events keep their pixel distance; when a frame contains only a
    /// fractional fixed-point delta, keep that fraction instead of dropping the frame.
    public static func fromMac(lines: Double, pixels: Double, continuous: Bool) -> Double {
        continuous ? (pixels != 0 ? pixels : lines) : lines * 40 / 3
    }
}

/// Accumulates wire scroll fractions between display frames. Injected Quartz events
/// only accept integer pixel deltas, so without a carry a slow trackpad or a burst of
/// tiny packets would lose motion and look like a stutter.
public struct ScrollAccumulator: Equatable {
    public private(set) var dx: Double = 0
    public private(set) var dy: Double = 0
    public init() {}
    public var hasPending: Bool { abs(dx) >= 1 || abs(dy) >= 1 }
    public mutating func add(dx: Double, dy: Double) {
        guard dx.isFinite, dy.isFinite else { return }
        self.dx = min(100_000, max(-100_000, self.dx + dx))
        self.dy = min(100_000, max(-100_000, self.dy + dy))
    }
    public mutating func takeWhole() -> (dx: Int, dy: Int) {
        let x = Self.whole(dx), y = Self.whole(dy)
        dx -= Double(x); dy -= Double(y)
        return (x, y)
    }
    public mutating func reset() { dx = 0; dy = 0 }
    private static func whole(_ value: Double) -> Int {
        guard value.isFinite else { return 0 }
        let rounded = value.rounded(.towardZero)
        if rounded >= Double(Int.max) { return Int.max }
        if rounded <= Double(Int.min) { return Int.min }
        return Int(rounded)
    }
}
