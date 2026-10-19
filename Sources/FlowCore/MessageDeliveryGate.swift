import Foundation

/// Bounds work waiting for the network queue and invalidates callbacks/sends from
/// a closed session, so a reconnect can never deliver an old key to a new peer.
public final class MessageDeliveryGate {
    private let lock=NSLock()
    private var epoch: UInt64=0
    private var pending=0
    private let capacity:Int
    public init(capacity:Int=512) { self.capacity=capacity }
    public var current:UInt64 { lock.lock();defer {lock.unlock()};return epoch }
    @discardableResult public func advance()->UInt64 { lock.lock();defer {lock.unlock()};epoch &+= 1;return epoch }
    public func valid(_ ticket:UInt64)->Bool { lock.lock();defer {lock.unlock()};return ticket==epoch }
    public func reserve()->UInt64? { lock.lock();defer {lock.unlock()};guard pending<capacity else{return nil};pending+=1;return epoch }
    public func release() { lock.lock();pending=max(0,pending-1);lock.unlock() }
}
