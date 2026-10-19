import Foundation

/// Device-relative coordinates: X to the right, Y down. Only live, confident
/// contacts are sent. No source-platform shortcuts or action names cross the wire.
public struct TouchpadContact:Codable,Equatable {
    public var id:Int
    public var x:Double
    public var y:Double
    public init(id:Int,x:Double,y:Double) { self.id=id;self.x=x;self.y=y }
    public var valid:Bool { (0...65535).contains(id) && x.isFinite && y.isFinite && (0...1).contains(x) && (0...1).contains(y) }
}

/// A stroke cannot begin midway through a focus/settings transition. Each begin
/// has exactly one end/cancel; lifting down to one finger ends gesture-only input.
public struct TouchpadSender {
    public private(set) var active=false
    private var waitForLift=false
    private var hasContacts=false
    public init() {}
    public mutating func cancel() -> Message? {
        waitForLift=hasContacts
        guard active else{return nil};active=false
        var m=Message("touchpad");m.phase="cancel";m.contacts=[];return m
    }
    public mutating func sample(_ contacts:[TouchpadContact],allowed:Bool) -> Message? {
        hasContacts = !contacts.isEmpty
        guard contacts.count<=5,contacts.allSatisfy({$0.valid}),Set(contacts.map(\.id)).count==contacts.count else { return cancel() }
        if contacts.isEmpty { waitForLift=false }
        if !allowed { return cancel() }
        if contacts.count<2 {
            guard active else{return nil};active=false;waitForLift = !contacts.isEmpty
            var m=Message("touchpad");m.phase="end";m.contacts=[];return m
        }
        guard !waitForLift else{return nil}
        var m=Message("touchpad");m.phase=active ? "change":"begin";m.contacts=contacts.sorted{$0.id<$1.id};active=true
        return m
    }
}

/// Mac's native event pipeline consumes recognized gesture deltas. Windows gets
/// the original contacts and lets its precision-touchpad stack recognize them.
public struct NativeTouchpadGesture:Equatable {
    public enum Kind:Equatable { case pan,magnify,rotate,dockHorizontal,dockVertical }
    public var kind:Kind
    public var phase:String
    public var dx:Double=0
    public var dy:Double=0
    public var value:Double=0
    public var velocity:Double=0
    public init(kind:Kind,phase:String,dx:Double=0,dy:Double=0,value:Double=0,velocity:Double=0) {
        self.kind=kind;self.phase=phase;self.dx=dx;self.dy=dy;self.value=value;self.velocity=velocity
    }
}
public struct TouchpadRecognizer {
    private var previous=[TouchpadContact](),origin=[TouchpadContact]()
    private var kind:NativeTouchpadGesture.Kind?
    private var active=false
    private var dockProgress=0.0
    private var dockVelocity=0.0,lastTime=0.0
    public init() {}
    public mutating func reset(cancelled:Bool=true) -> [NativeTouchpadGesture] {
        defer { previous=[];origin=[];kind=nil;active=false;dockProgress=0;dockVelocity=0;lastTime=0 }
        guard let kind,active else{return []}
        return [.init(kind:kind,phase:cancelled ? "cancel":"end",value:dockProgress,velocity:cancelled ? 0:dockVelocity)]
    }
    public mutating func receive(_ m:Message,at time:Double=ProcessInfo.processInfo.systemUptime) -> [NativeTouchpadGesture] {
        guard m.type=="touchpad",(try? m.validate()) != nil else{return reset()}
        if m.phase=="end" || m.phase=="cancel" {
            if time-lastTime>0.1 { dockVelocity=0 }
            return reset(cancelled:m.phase=="cancel")
        }
        let elapsed=time-lastTime
        defer { lastTime=time }
        let points=m.contacts!.sorted{$0.id<$1.id}
        if m.phase=="begin" {
            let cancelled=reset();active=true;previous=points;origin=points;return cancelled
        }
        guard active else{return []}
        if points.map(\.id) != previous.map(\.id) {
            let cancelled=reset();active=true;previous=points;origin=points;return cancelled
        }
        func center(_ p:[TouchpadContact])->(Double,Double) { (p.map(\.x).reduce(0,+)/Double(p.count),p.map(\.y).reduce(0,+)/Double(p.count)) }
        func distance(_ p:[TouchpadContact])->Double { hypot(p[1].x-p[0].x,(p[1].y-p[0].y)*0.6) }
        func angle(_ p:[TouchpadContact])->Double { atan2((p[1].y-p[0].y)*0.6,p[1].x-p[0].x) }
        func turn(_ d:Double)->Double { atan2(sin(d),cos(d)) }
        let c=center(points),o=center(origin)
        var result=[NativeTouchpadGesture]()
        if kind==nil {
            if points.count>=3 {
                if hypot(c.0-o.0,c.1-o.1)>0.012 { kind=abs(c.0-o.0)>=abs(c.1-o.1) ? .dockHorizontal:.dockVertical }
            } else {
                let zoom=log(max(0.001,distance(points))/max(0.001,distance(origin)))
                let rotation=turn(angle(points)-angle(origin))
                let pan=hypot(c.0-o.0,(c.1-o.1)*0.6)
                if abs(zoom)>0.025 && abs(zoom)>pan*3 { kind = .magnify }
                else if abs(rotation)>0.06 && abs(rotation)>pan*3 { kind = .rotate }
                else if pan>0.008 { kind = .pan }
            }
            guard let kind else{return []}
            result.append(.init(kind:kind,phase:"begin"))
            previous=origin // Include movement accumulated during recognition.
        }
        let p=center(previous),kind=kind!
        switch kind {
        case .pan: result.append(.init(kind:kind,phase:"change",dx:c.0-p.0,dy:c.1-p.1))
        case .magnify: result.append(.init(kind:kind,phase:"change",value:distance(points)/max(0.001,distance(previous))-1))
        case .rotate: result.append(.init(kind:kind,phase:"change",value: -turn(angle(points)-angle(previous))*180 / .pi))
        case .dockHorizontal: result.append(.init(kind:kind,phase:"change",value: -(c.0-o.0)))
        case .dockVertical: result.append(.init(kind:kind,phase:"change",value: -(c.1-o.1)))
        }
        if kind == .dockHorizontal || kind == .dockVertical {
            let progress=result.last?.value ?? 0
            dockVelocity=elapsed>0 && elapsed<0.2 ? max(-150,min(150,(progress-dockProgress)/elapsed*65)):0
            dockProgress=progress
        }
        previous=points
        return result
    }
}
