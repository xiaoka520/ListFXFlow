import Foundation

public enum LoginSession {
    /// Like ThunderDisplay: no confirmed console user means the login window;
    /// a missing console dictionary must not leave the agent permanently blocked.
    public static func isLoggedIn(name:String?,uid:UInt32)->Bool {
        guard let name else{return false}
        return uid>=500 && name != "loginwindow" && name != "_mbsetupuser"
    }
}

public struct LoginStatus:Codable,Equatable {
    public enum Phase:String,Codable { case starting,waitingBridge,waitingDisplay,checking,listening,connected,blocked,stopped }
    public var phase:Phase
    public var detail:String
    public var previousPhase:Phase?
    public var previousDetail:String?
    public var recordedAt:Date
    public var uptime:Double
    public var pid:Int32
    public init(phase:Phase,detail:String,uptime:Double,pid:Int32,recordedAt:Date=Date()) {
        self.phase=phase;self.detail=String(detail.prefix(256));self.uptime=uptime;self.pid=pid;self.recordedAt=recordedAt
    }
    public mutating func update(_ phase:Phase,detail:String,uptime:Double,now:Date=Date()) {
        if phase == .stopped && self.phase != .stopped { previousPhase=self.phase;previousDetail=self.detail }
        self.phase=phase;self.detail=String(detail.prefix(256));self.uptime=uptime;recordedAt=now
    }
    public var valid:Bool { pid>0 && uptime.isFinite && uptime>=0 && detail.utf8.count<=1024 && (previousDetail?.utf8.count ?? 0)<=1024 && recordedAt.timeIntervalSince1970.isFinite }
    public func fresh(uptime now:Double,date:Date=Date(),maximumAge:Double=5)->Bool {
        valid && maximumAge>0 && now>=uptime && now-uptime<maximumAge && date.timeIntervalSince(recordedAt)>=0 && date.timeIntervalSince(recordedAt)<maximumAge
    }
}
