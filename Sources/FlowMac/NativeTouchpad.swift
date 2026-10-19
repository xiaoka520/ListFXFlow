import AppKit
import FlowCore

/// Builds native Cocoa/Quartz gestures, never keyboard shortcuts. The gesture
/// fields are the Quartz format used by Hammerspoon and native Dock swipes.
enum NativeTouchpad {
    private static let source=CGEventSource(stateID:.combinedSessionState)
    static func event(_ g:NativeTouchpadGesture,at point:CGPoint,augmentDock:Bool=ProcessInfo.processInfo.operatingSystemVersion.majorVersion>=27)->CGEvent? {
        let phase:Int64 = g.phase=="begin" ? 1:g.phase=="change" ? 2:g.phase=="end" ? 4:8
        var event:CGEvent?
        if g.kind == .pan {
            event=CGEvent(scrollWheelEvent2Source:source,units:.pixel,wheelCount:2,
                          wheel1:Int32(clamping:Int((g.dy*800).rounded())),wheel2:Int32(clamping:Int((g.dx*1200).rounded())),wheel3:0)
            event?.setIntegerValueField(.scrollWheelEventIsContinuous,value:1)
            event?.setIntegerValueField(.scrollWheelEventScrollPhase,value:phase)
            event?.setIntegerValueField(.scrollWheelEventMomentumPhase,value:0)
            event?.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1,value:g.dy*800/10)
            event?.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis2,value:g.dx*1200/10)
        } else {
            event=CGEvent(source:source)
            func integer(_ field:UInt32,_ value:Int64) { event?.setIntegerValueField(CGEventField(rawValue:field)!,value:value) }
            func number(_ field:UInt32,_ value:Double) { event?.setDoubleValueField(CGEventField(rawValue:field)!,value:value) }
            integer(55,29);integer(132,phase)
            event?.flags=CGEventFlags(rawValue:0x100)
            integer(111,0);integer(112,0);integer(133,0);number(139,0);number(140,0)
            switch g.kind {
            case .magnify: integer(110,8);number(113,g.value)
            case .rotate: integer(110,5);number(114,g.value)
            case .dockHorizontal,.dockVertical:
                integer(55,30);integer(110,23);integer(123,g.kind == .dockHorizontal ? 1:2)
                number(124,g.value);number(129,g.velocity);number(130,g.velocity)
                integer(134,phase);number(138,3);number(169,Double(mach_absolute_time()))
                integer(135,Int64(Int32(bitPattern:Float(g.value).bitPattern)))
            case .pan: break
            }
        }
        event?.location=point
        event?.setIntegerValueField(.eventSourceUserData,value:InputEngine.marker)
        if let base=event,g.kind == .magnify || g.kind == .rotate {
            guard let payload=TouchpadEventPayload.digitizer(timestamp:base.timestamp) else{return nil}
            event=TouchpadEventPayload.attach(payload,to:base)
        } else if let base=event,augmentDock,g.kind == .dockHorizontal || g.kind == .dockVertical {
            event=TouchpadEventPayload.attach(TouchpadEventPayload.dock(phase:UInt32(phase),motion:g.kind == .dockHorizontal ? 1:2,progress:g.value,velocity:g.velocity,timestamp:base.timestamp),to:base)
        }
        // CGEvent reconstruction drops source user-data. Restore it afterwards
        // so our capture tap never forwards the injected gesture back again.
        event?.location=point
        event?.setIntegerValueField(.eventSourceUserData,value:InputEngine.marker)
        return event
    }
}

final class MacTouchpadCapture {
    var send:((Message)->Void)?
    private var sender=TouchpadSender()
    private var identities=[AnyHashable:Int]()
    var active:Bool { sender.active }
    func cancel() {
        if let m=sender.cancel() { send?(m) };identities.removeAll()
    }
    /// Called for Quartz's generic gesture event; Cocoa exposes the live contacts.
    func sample(_ event:CGEvent,allowed:Bool)->Bool {
        guard let native=NSEvent(cgEvent:event) else{return false}
        let all=native.allTouches()
        if all.isEmpty {
            if !native.phase.intersection([.ended,.cancelled]).isEmpty {
                let old=active
                if let m=sender.sample([],allowed:allowed) { send?(m) };identities.removeAll()
                return allowed && old
            }
            return allowed && active
        }
        let touches=all.filter { $0.type == .indirect && !$0.isResting && !$0.phase.intersection(.touching).isEmpty }
        let oldActive=active
        var contacts=[TouchpadContact]()
        for touch in touches {
            guard let identity=touch.identity as? AnyHashable else { cancel();return false }
            if identities[identity]==nil {
                guard let slot=(0..<5).first(where:{ !identities.values.contains($0) }) else { cancel();return false }
                identities[identity]=slot
            }
            let p=touch.normalizedPosition
            contacts.append(.init(id:identities[identity]!,x:p.x,y:1-p.y))
        }
        if let m=sender.sample(contacts,allowed:allowed) { send?(m) }
        if touches.isEmpty { identities.removeAll() }
        return allowed && (oldActive || active)
    }
}
