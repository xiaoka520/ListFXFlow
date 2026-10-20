import Foundation

/// Keeps startup/rebinding retries separate from the user's stop intent.
public struct BridgeRecovery {
    public enum Action:Equatable { case none, waitForAddress, start }
    public private(set) var requested=false
    private var bound:Set<String>=[]
    private var retryAt=0.0
    public init() {}
    public mutating func request() { requested=true }
    public mutating func stop() { requested=false;bound=[];retryAt=0 }
    public mutating func started(addresses:Set<String>) { bound=addresses;retryAt=0 }
    public mutating func failed(now:Double) { bound=[];retryAt=now+1 }
    /// Rebuild input/listeners after display reconfiguration without enabling a
    /// deliberately stopped session. Repeated notifications postpone the retry.
    @discardableResult public mutating func restartAfterDisplayChange(now:Double)->Bool {
        guard requested else { return false }
        bound=[];retryAt=now+1
        return true
    }
    public func action(addresses:Set<String>,listening:Bool,now:Double,activeAddress:String?=nil)->Action {
        guard requested else{return .none}
        guard !addresses.isEmpty else{return .waitForAddress}
        if listening,let activeAddress,addresses.contains(activeAddress) { return .none }
        if listening && bound==addresses { return .none }
        return now>=retryAt ? .start:.none
    }
}
