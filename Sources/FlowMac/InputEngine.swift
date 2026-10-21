import AppKit
import ApplicationServices
import FlowCore
import FlowHID
import OSLog

/// All input state lives on the main queue. Pointer samples and wheel fractions are
/// coalesced into display frames so a high-polling mouse cannot flood network or
/// injection, and so pointer motion is always delivered before wheel motion.
final class InputEngine {
    private let loginWindow:Bool
    private var sessionButtons:UInt8=0
    private var injectionUnavailable=false
    var injectionFailure:(()->Void)?
    static let marker: Int64 = 0x4c465846
    let router = PointerRouter()
    var send: ((Message) -> Void)?
    var emergency: (() -> Void)?
    var onFocus: ((String) -> Void)?
    var peerSupportsCaps = false
    var peerSupportsGameMode=false
    var onGameMode:((Bool,String)->Void)?
    private var gameShortcut=GameShortcut()
    func toggleGameProtection() {
        guard enabled,peerSupportsGameMode,!loginWindow,router.active=="windows" else{return}
        flushOutbound()
        var message=Message("gameModeRequest");message.down = !router.crossingLocked;send?(message)
    }
    var peerSupportsTouchpad=false
    var peerReceivesTouchpad=false
    var peerSupportsTouchpadFeedback=false
    var peerTouchpadEnabled=false { didSet { if !peerTouchpadEnabled { cancelTouchpad() } } }
    var touchpadEnabled=false { didSet { if !touchpadEnabled { cancelTouchpad() };sendTouchpadSetting() } }
    private let touchpadCapture=MacTouchpadCapture()
    private var touchpadEpoch:Int64=1
    private lazy var touchpadReceiver=MacGestureReceiver { [weak self] in self?.postTouchpad($0) ?? false }
    var onTouchpadActivity:((String)->Void)?
    var touchpadSessionActive=true { didSet { if !touchpadSessionActive { cancelTouchpad() } } }
    private var suppressTouchpadMomentum=false
    private var touchpadAllowed:Bool { TouchpadAvailability.receivesWindowsGestures && !loginWindow && touchpadSessionActive && enabled && touchpadEnabled && peerTouchpadEnabled }
    func sendTouchpadSetting() {
        guard enabled,peerSupportsTouchpad,!loginWindow else{return}
        var m=Message("touchpadControl");m.down=TouchpadAvailability.receivesWindowsGestures && touchpadEnabled && touchpadSessionActive;send?(m)
    }
    private func cancelTouchpad() {
        touchpadCapture.cancel();suppressTouchpadMomentum=false
        touchpadReceiver.reset();touchpadEpoch = touchpadEpoch==Int64.max ? 1:touchpadEpoch+1
    }
    @discardableResult private func postTouchpad(_ gesture:NativeTouchpadGesture)->Bool {
        guard let events=NativeTouchpad.events(gesture,at:CGEvent(source:nil)?.location ?? lastPoint) else{
            onTouchpadActivity?("已收到触点，但无法生成本机手势事件");return false
        }
        for event in events { event.post(tap:.cgSessionEventTap) }
        if gesture.phase=="begin" {
            let name:String
            switch gesture.kind {
            case .pan: name="双指滚动"
            case .magnify: name="双指缩放"
            case .rotate: name="双指旋转"
            case .dockHorizontal: name="多指横向滑动"
            case .dockVertical: name="多指纵向滑动"
            }
            onTouchpadActivity?("已接收并提交事件 · "+name)
        }
        return true
    }
    var enabled = false { didSet {
        if !enabled {
            stopFrameTimer()
            clearFrameInput()
            if let activity { ProcessInfo.processInfo.endActivity(activity); self.activity = nil }
            reset()
        } else {
            if activity == nil { activity = ProcessInfo.processInfo.beginActivity(options: [.userInitiated,.idleSystemSleepDisabled,.idleDisplaySleepDisabled,.latencyCritical],reason: "雷雳键鼠共享 · 无人值守") }
            if loginWindow {
                physicalKeys=[];physicalButtons=[];capsState=false
            } else {
            physicalKeys = Set(KeyMap.entries.filter { CGEventSource.keyState(.hidSystemState, key: CGKeyCode($0.mac)) }.map(\.hid))
            physicalKeys.remove(57) // Caps Lock is a toggle, not a held key.
            physicalButtons = Set((0...4).filter { CGEventSource.buttonState(.hidSystemState, button: CGMouseButton(rawValue: UInt32($0))!) })
            capsState = CGEventSource.flagsState(.hidSystemState).contains(.maskAlphaShift)
            }
        }
    } }
    private var frameTimer: DispatchSourceTimer?
    private var lastFrameActivity = 0.0
    private var lastOutboundPointerSent = 0.0
    private var lastOutboundScrollSent = 0.0
    private var outboundPointer: Message?
    private var outboundScrollDX = 0.0
    private var outboundScrollDY = 0.0
    private var outboundRelativeDX=0.0,outboundRelativeDY=0.0
    private var inboundPointer: Message?
    private var inboundScroll = ScrollAccumulator()
    private var activity: NSObjectProtocol?
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private let cursorCapture = CursorCapture()
    private let eventSource:CGEventSource?
    // Reuse the keyboard source across lock and ordinary key events. Pointer
    // injection keeps its separate source and cannot mutate this keyboard state.
    private let keyboardSource:CGEventSource?
    init(loginWindow:Bool=false) {
        self.loginWindow=loginWindow
        if loginWindow { eventSource=nil;keyboardSource=nil }
        else {
            let source=CGEventSource(stateID:.privateState);source?.localEventsSuppressionInterval=0
            eventSource=source;keyboardSource=CGEventSource(stateID:.combinedSessionState)
        }
    }
    private var capsState = false
    private var hidConnection: UInt32 = 0
    private var keyboardEcho = KeyboardEcho()
    private var physicalKeys = Set<Int>()
    private var physicalButtons = Set<Int>()
    private var injectedKeys = Set<Int>()
    private var injectedButtons = Set<Int>()
    private var lastPoint: CGPoint = .zero
    private let pointerLogger=Logger(subsystem:"com.listfxflow.mac",category:"pointer-seam")
    private let inputLogger=Logger(subsystem:"com.listfxflow.mac",category:"input-state")
    private var lastSeamDiagnostic=0.0
    private var clicks: [Int: (time: TimeInterval, point: CGPoint, count: Int)] = [:]
    var bounds: CGRect { CGDisplayBounds(CGMainDisplayID()) }

    func start() throws {
        guard tap == nil else { return }
        injectionUnavailable=false
        if loginWindow {
            guard FlowPreLoginMarkerPresent(),CGPreflightPostEventAccess(),CGPreflightListenEventAccess() else { throw FlowMacError.permission }
        } else { guard AXIsProcessTrusted(),CGPreflightListenEventAccess() else { throw FlowMacError.permission } }
        touchpadCapture.send={ [weak self] m in
            guard let self else{return};if m.phase=="begin" { self.suppressTouchpadMomentum=true;self.touchpadEpoch = self.touchpadEpoch==Int64.max ? 1:self.touchpadEpoch+1 }
            var message=m;message.gestureEpoch=self.touchpadEpoch;self.flushOutbound();self.send?(message)
        }
        cursorCapture.onLost = { [weak self] in
            guard let self else{return}
            self.inputLogger.error("stop-cause=cursor-parking-failed; three consecutive native warp failures")
            self.enabled=false
            if self.loginWindow { self.injectionFailure?() } else { self.emergency?() }
        }
        let types: [CGEventType] = [.mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged,
            .leftMouseDown,.leftMouseUp,.rightMouseDown,.rightMouseUp,.otherMouseDown,.otherMouseUp,
            .keyDown,.keyUp,.flagsChanged,.scrollWheel]
        var mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
        if !loginWindow && TouchpadAvailability.forwardingEnabled { mask |= (1 << 29) | (1 << 30) }
        let reference = Unmanaged.passUnretained(self).toOpaque()
        guard let tap = CGEvent.tapCreate(tap: .cghidEventTap, place: .headInsertEventTap,
            options: .defaultTap, eventsOfInterest: mask, callback: { _, type, event, ref in
                guard let ref else { return Unmanaged.passUnretained(event) }
                return Unmanaged<InputEngine>.fromOpaque(ref).takeUnretainedValue().handle(type, event)
            }, userInfo: reference) else { throw FlowMacError.permission }
        self.tap = tap
        source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }
    private func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            inputLogger.error("stop-cause=\(type == .tapDisabledByTimeout ? "event-tap-timeout":"event-tap-disabled",privacy:.public)")
            enabled = false
            if loginWindow { injectionFailure?() } else { emergency?() }
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        guard event.getIntegerValueField(.eventSourceUserData) != Self.marker else { return Unmanaged.passUnretained(event) }
        if loginWindow,event.getIntegerValueField(.eventSourceUnixProcessID)==Int64(getpid()) { return Unmanaged.passUnretained(event) }
        guard enabled else { return Unmanaged.passUnretained(event) }
        if !loginWindow,(type.rawValue==29 || type.rawValue==30) {
            let remote=router.active != "mac"
            guard touchpadAllowed,peerReceivesTouchpad else { return Unmanaged.passUnretained(event) }
            let consumed=type.rawValue==29 && touchpadCapture.sample(event,allowed:remote && touchpadAllowed && peerReceivesTouchpad)
            if consumed || (remote && touchpadAllowed && peerReceivesTouchpad && (type.rawValue==30 || touchpadCapture.active)) { return nil }
            return Unmanaged.passUnretained(event)
        }
        let code = Int(event.getIntegerValueField(.keyboardEventKeycode))
        if type == .keyDown, code == 53, event.flags.contains([.maskControl,.maskAlternate]) {
            inputLogger.notice("stop-cause=emergency-shortcut")
            enabled = false; emergency?(); return nil
        }
        if [.keyDown,.keyUp,.flagsChanged].contains(type), keyboardEcho.consume(
            code: code, type: type.rawValue, flags: event.flags.intersection([.maskAlphaShift,.maskShift,.maskControl,.maskAlternate,.maskCommand]).rawValue,
            repeated: event.getIntegerValueField(.keyboardEventAutorepeat) != 0, now: ProcessInfo.processInfo.systemUptime) {
            event.setIntegerValueField(.eventSourceUserData, value: Self.marker)
            return Unmanaged.passUnretained(event)
        }
        if code==37 && (type == .keyDown || type == .keyUp) {
            let action=gameShortcut.process(down:type == .keyDown,chord:event.flags.contains([.maskControl,.maskAlternate]),
                available:peerSupportsGameMode && !loginWindow && router.active=="windows")
            if action.toggle { toggleGameProtection() }
            if action.consume { return nil }
        }
        // Caps Lock is a system toggle, not a held key. Let the local OS apply the
        // physical event and publish the resulting state so both desktops match.
        if type == .flagsChanged, code == 57 {
            let state = event.flags.contains(.maskAlphaShift)
            if state != capsState {
                capsState = state
                if peerSupportsCaps { var m = Message("caps"); m.down = state; send?(m) }
            }
            return Unmanaged.passUnretained(event)
        }
        let oldActive = router.active
        let remote = oldActive != "mac"
        switch type {
        case .mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged:
            let p = event.location; let r = bounds
            if remote && router.crossingLocked {
                // Locked remote games receive physical deltas without clipping
                // them at a virtual desktop edge or imposing an absolute cursor.
                outboundRelativeDX+=event.getDoubleValueField(.mouseEventDeltaX)
                outboundRelativeDY+=event.getDoubleValueField(.mouseEventDeltaY)
                let now=ProcessInfo.processInfo.systemUptime
                if now-lastOutboundPointerSent>=0.004 { flushOutbound() }
                startFrameTimer();cursorCapture.pinIfNeeded();return nil
            }
            // The event cache can outlive a lost key/button-up (sleep, permission
            // changes, or a handoff). Reconcile only on the local desktop: remote
            // physical events are suppressed and need their event-based cache.
            if !remote {
                InputHoldReconciler.reconcile(keys:&physicalKeys,buttons:&physicalButtons,
                    keyDown:{ hid in KeyMap.byHID[hid].map { CGEventSource.keyState(.hidSystemState,key:CGKeyCode($0)) } ?? false },
                    buttonDown:{ CGEventSource.buttonState(.hidSystemState,button:CGMouseButton(rawValue:UInt32($0))!) })
            }
            let dx=event.getDoubleValueField(.mouseEventDeltaX),dy=event.getDoubleValueField(.mouseEventDeltaY)
            let m = router.move(local: "mac", x: p.x-r.minX, y: p.y-r.minY,
                dx: dx, dy: dy,
                width: r.width, height: r.height, mayCross: physicalKeys.isEmpty && physicalButtons.isEmpty)
            if !remote { diagnoseSeam(point:p,bounds:r,dx:dx,dy:dy,crossed:router.active != oldActive) }
            if router.active != oldActive {
                flushOutbound()
                changeFocus(localSample: p)
                // The ownership transition must not wait for a frame.
                send?(m)
            } else {
                // Keep the other router at the physical cursor even while this
                // desktop owns it. Switching source devices must not resume from
                // the last remote-only position on a different part of the seam.
                outboundPointer = m
                // Send the newest position immediately when the 4 ms slot is free;
                // the timer only has to catch up with faster raw samples. This keeps
                // pointer latency from depending on a busy scroll event tap.
                let now = ProcessInfo.processInfo.systemUptime
                if now - lastOutboundPointerSent >= 0.004 {
                    outboundPointer = nil
                    lastOutboundPointerSent = now
                    send?(m)
                }
                startFrameTimer()
            }
            if router.active != "mac" { cursorCapture.pinIfNeeded(); return nil }
            // A return transition suppresses the source event and places the cursor at the seam.
            if remote { injectPointer(m); return nil }
        case .keyDown,.keyUp,.flagsChanged:
            guard let hid = KeyMap.byMac[code] else { return remote ? nil : Unmanaged.passUnretained(event) }
            let down:Bool
            if type != .flagsChanged { down=type == .keyDown }
            else if loginWindow {
                let flag:CGEventFlags = [56,60].contains(code) ? .maskShift:[59,62].contains(code) ? .maskControl:[58,61].contains(code) ? .maskAlternate:.maskCommand
                down=event.flags.contains(flag)
            } else { down=CGEventSource.keyState(.hidSystemState,key:CGKeyCode(code)) }
            if down { physicalKeys.insert(hid) } else { physicalKeys.remove(hid) }
            if remote {
                flushOutbound()
                var m = Message("key"); m.key = hid; m.down = down; send?(m); return nil
            }
        case .leftMouseDown,.leftMouseUp,.rightMouseDown,.rightMouseUp,.otherMouseDown,.otherMouseUp:
            let button = Int(event.getIntegerValueField(.mouseEventButtonNumber))
            let down = [.leftMouseDown,.rightMouseDown,.otherMouseDown].contains(type)
            if down { physicalButtons.insert(button) } else { physicalButtons.remove(button) }
            if remote, button <= 4 {
                flushOutbound()
                var m = Message("button"); m.button = button; m.down = down; send?(m); return nil
            }
        case .scrollWheel:
            if remote {
                if touchpadAllowed {
                    if touchpadCapture.active || (suppressTouchpadMomentum && event.getIntegerValueField(.scrollWheelEventMomentumPhase) != 0) { return nil }
                    if event.getIntegerValueField(.scrollWheelEventScrollPhase)==1 { suppressTouchpadMomentum=false }
                }
                let continuous = event.getIntegerValueField(.scrollWheelEventIsContinuous) != 0
                // A conventional Mac notch is three lines, not three Windows notches.
                func delta(_ fixed: CGEventField, _ integer: CGEventField, _ pixel: CGEventField) -> Double {
                    let pixels = Double(event.getIntegerValueField(pixel))
                    let precise = event.getDoubleValueField(fixed)
                    return ScrollUnits.fromMac(lines: precise != 0 ? precise : Double(event.getIntegerValueField(integer)),
                                               pixels: pixels, continuous: continuous)
                }
                outboundScrollDX += delta(.scrollWheelEventFixedPtDeltaAxis2,.scrollWheelEventDeltaAxis2,.scrollWheelEventPointDeltaAxis2)
                outboundScrollDY += delta(.scrollWheelEventFixedPtDeltaAxis1,.scrollWheelEventDeltaAxis1,.scrollWheelEventPointDeltaAxis1)
                let now = ProcessInfo.processInfo.systemUptime
                if now - lastOutboundScrollSent >= 0.004 {
                    flushOutbound(includeScroll: true)
                }
                startFrameTimer()
                return nil
            }
        default: break
        }
        return Unmanaged.passUnretained(event)
    }
    private func startFrameTimer() {
        guard enabled else { return }
        lastFrameActivity = ProcessInfo.processInfo.systemUptime
        guard frameTimer == nil else { return }
        let timer = DispatchSource.makeTimerSource(queue: .main)
        timer.schedule(deadline: .now(), repeating: .milliseconds(4), leeway: .milliseconds(1))
        timer.setEventHandler { [weak self] in self?.flushFrame() }
        timer.resume()
        frameTimer = timer
    }
    private func stopFrameTimer() { frameTimer?.cancel(); frameTimer = nil }
    private var hasPendingFrameInput: Bool {
        outboundPointer != nil || outboundRelativeDX != 0 || outboundRelativeDY != 0 || outboundScrollDX != 0 || outboundScrollDY != 0 || inboundPointer != nil || inboundScroll.hasPending
    }
    private func flushFrame() {
        guard enabled else { stopFrameTimer(); return }
        let now = ProcessInfo.processInfo.systemUptime
        // A single source-side 4 ms coalescing window; the receiver does not
        // add another wheel timer. Real events keep their cadence with no tail.
        flushOutbound(includeScroll:router.active != "mac")
        if router.active == "mac" { flushInbound() }
        if !hasPendingFrameInput && now - lastFrameActivity > 0.05 { stopFrameTimer() }
    }
    private func diagnoseSeam(point:CGPoint,bounds:CGRect,dx:Double,dy:Double,crossed:Bool) {
        let side=router.topology.side
        let outward:Bool
        switch side {
        case .left: outward=point.x<=bounds.minX+2 && dx<0
        case .right: outward=point.x>=bounds.maxX-2 && dx>0
        case .top: outward=point.y<=bounds.minY+2 && dy<0
        case .bottom: outward=point.y>=bounds.maxY-2 && dy>0
        }
        guard outward else{return}
        let now=ProcessInfo.processInfo.systemUptime
        guard crossed || now-lastSeamDiagnostic>=1 else{return};lastSeamDiagnostic=now
        let result=crossed ? "crossed":(!physicalKeys.isEmpty || !physicalButtons.isEmpty ? "held-input":"outside-overlap")
        // Coordinates and counts only; never record key codes or typed text.
        pointerLogger.notice("seam=\(side.rawValue,privacy:.public) result=\(result,privacy:.public) x=\(point.x,privacy:.public) y=\(point.y,privacy:.public) dx=\(dx,privacy:.public) dy=\(dy,privacy:.public) keys=\(self.physicalKeys.count,privacy:.public) buttons=\(self.physicalButtons.count,privacy:.public)")
    }
    private func flushOutbound(includeScroll: Bool = true) {
        if let pointer = outboundPointer {
            outboundPointer = nil
            lastOutboundPointerSent = ProcessInfo.processInfo.systemUptime
            send?(pointer)
        }
        while outboundRelativeDX != 0 || outboundRelativeDY != 0 {
            let dx=min(1_000_000,max(-1_000_000,outboundRelativeDX)),dy=min(1_000_000,max(-1_000_000,outboundRelativeDY))
            var message=Message("relativePointer");message.dx=dx;message.dy=dy
            outboundRelativeDX-=dx;outboundRelativeDY-=dy
            lastOutboundPointerSent=ProcessInfo.processInfo.systemUptime;send?(message)
        }
        if includeScroll { flushOutboundScroll() }
    }
    private func flushOutboundScroll() {
        if outboundScrollDX != 0 || outboundScrollDY != 0 {
            var message = Message("scroll")
            message.dx = outboundScrollDX; message.dy = outboundScrollDY
            outboundScrollDX = 0; outboundScrollDY = 0
            lastOutboundScrollSent = ProcessInfo.processInfo.systemUptime
            send?(message)
        }
    }
    private func flushInbound(includeScroll: Bool = true) {
        if let pointer = inboundPointer { inboundPointer = nil; injectPointer(pointer) }
        if includeScroll { flushInboundScroll() }
    }
    private func flushInboundScroll() {
        let delta = inboundScroll.takeWhole()
        if delta.dx != 0 || delta.dy != 0 { injectScroll(dx: delta.dx, dy: delta.dy) }
    }
    private func clearFrameInput() {
        outboundPointer = nil; outboundScrollDX = 0; outboundScrollDY = 0
        outboundRelativeDX=0;outboundRelativeDY=0
        inboundPointer = nil; inboundScroll.reset()
    }
    func receive(_ message: Message) {
        guard enabled || message.type == "reset" else { return }
        switch message.type {
        case "gameMode" where peerSupportsGameMode && !loginWindow:
            clearFrameInput()
            let old=router.active
            router.crossingLocked=false
            if message.down==true || router.active=="windows" { router.accept(message) }
            router.crossingLocked=message.down==true
            if old != router.active { changeFocus() }
            onGameMode?(router.crossingLocked,message.phase!)
        case "pointer":
            let old = router.active
            guard router.accept(message) else{return}
            outboundPointer = nil; outboundScrollDX = 0; outboundScrollDY = 0
            if old != router.active { changeFocus() }
            guard router.active == "mac" else { return }
            if old != "mac" {
                // First sample after a handoff must land immediately at the seam.
                inboundPointer = nil; inboundScroll.reset()
                injectPointer(message)
            } else {
                inboundPointer = message
                startFrameTimer()
            }
        case "key" where router.active == "mac":
            flushInbound(includeScroll: false)
            injectKey(message.key!, down: message.down!)
        case "button" where router.active == "mac":
            flushInbound(includeScroll: true)
            injectButton(message.button!, down: message.down!)
        case "scroll" where router.active == "mac":
            flushInbound(includeScroll: false)
            inboundScroll.add(dx: message.dx!, dy: message.dy!)
            flushInboundScroll()
        case "touchpadControl":
            peerTouchpadEnabled=peerSupportsTouchpad && message.down==true
        case "touchpad" where touchpadAllowed && router.active=="mac":
            flushInbound()
            if message.phase=="begin" { onTouchpadActivity?("已收到 \(message.contacts?.count ?? 0) 指触点 · 识别中") }
            if let feedback=touchpadReceiver.receive(message),peerSupportsTouchpadFeedback { send?(feedback) }
        case "touchpadFeedback" where touchpadAllowed && peerSupportsTouchpadFeedback && router.active != "mac" && message.gestureEpoch==touchpadEpoch:
            onTouchpadActivity?("远端已收 \(message.frames ?? 0) 帧 / 已提交 \(message.events ?? 0) 个事件")
        case "caps":
            if let state = message.down {
                if capsState != state {
                    flushInbound(includeScroll: false)
                    capsState = state
                    setCaps(state)
                }
            }
        case "reset": reset()
        default: break
        }
    }
    private func changeFocus(localSample:CGPoint?=nil) {
        cancelTouchpad()
        releaseInjected()
        // Clear physical holds on the old desktop even if another device caused the switch.
        for hid in physicalKeys { keyEvent(hid, down: false) }
        for button in physicalButtons { buttonEvent(button, down: false) }
        physicalKeys.removeAll(); physicalButtons.removeAll()
        clearFrameInput()
        let remote = router.active != "mac"
        if remote {
            cursorCapture.begin(at:PointerParking.anchor(sample:localSample ?? lastPoint,bounds:bounds,side:router.topology.side))
        } else {
            let r=bounds,t=router.topology.mac,p=router.position
            cursorCapture.end(at:CGPoint(x:r.minX+(p.x-t.x)/t.width*(r.width-1),y:r.minY+(p.y-t.y)/t.height*(r.height-1)))
        }
        onFocus?(router.active)
    }
    func sendCapsState() {
        guard enabled else { return }
        var message = Message("caps"); message.down = capsState
        send?(message)
    }
    private func setCaps(_ enabled: Bool) {
        if loginWindow { postKeyboard(code:57,down:enabled,modifier:true);return }
        // Deskflow posts Caps as NX_FLAGSCHANGED, never as a key click or a
        // system-defined notification. A flagsChanged event updates the modifier
        // state without invoking the Caps-to-English input-source shortcut.
        if hidConnection == 0 { hidConnection = FlowHIDOpen() }
        _ = FlowHIDSetCaps(hidConnection, enabled)
        postKeyboard(code: 57, down: enabled, modifier: true)
    }
    private func postKeyboard(code: Int, down: Bool, modifier: Bool, repeated: Bool = false) {
        guard !injectionUnavailable else{return}
        let flags = modifierFlags
        if loginWindow {
            guard CGPreflightPostEventAccess(),let event=CGEvent(keyboardEventSource:nil,virtualKey:UInt16(code),keyDown:down) else { failPosting();return }
            event.flags=flags
            event.setIntegerValueField(.keyboardEventAutorepeat,value:repeated ? 1:0)
            if modifier { event.type = .flagsChanged }
            event.setIntegerValueField(.eventSourceUserData,value:Self.marker)
            event.post(tap:.cgSessionEventTap)
            return
        }
        // Deskflow's macOS path: modifier changes are NX_FLAGSCHANGED carrying the
        // global flags; ordinary keys are NX_KEYDOWN/NX_KEYUP with flags=0 and
        // options=0, so the driver and the active IME translate them normally.
        if hidConnection == 0 { hidConnection = FlowHIDOpen() }
        let native=FlowHIDMakeKey(UInt16(code),down,modifier,repeated,UInt32(truncatingIfNeeded:flags.rawValue))
        if FlowHIDPostKey(hidConnection, native) {
            let type: CGEventType = modifier ? .flagsChanged : (down ? .keyDown : .keyUp)
            let echoFlags: CGEventFlags = modifier ? flags : []
            keyboardEcho.expect(code: code, type: type.rawValue, flags: echoFlags.rawValue, repeated: repeated,
                                now: ProcessInfo.processInfo.systemUptime)
            return
        }
        if let event = KeyboardEventFactory.make(code:UInt16(code),down:down,repeated:repeated,
                                                flags:flags,source:keyboardSource) {
            if modifier { event.type = .flagsChanged }
            // Preserve Quartz-generated keyboard flags. The generic mouse post()
            // deliberately replaces flags and must not be used for keyboard input.
            event.setIntegerValueField(.eventSourceUserData,value:Self.marker)
            event.post(tap:.cghidEventTap)
        }
    }
    private func post(_ event: CGEvent) {
        guard !injectionUnavailable else{return}
        event.setIntegerValueField(.eventSourceUserData, value: Self.marker)
        event.flags = modifierFlags
        if loginWindow {
            guard CGPreflightPostEventAccess() else { failPosting();return }
            switch event.type {
            case .mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged,.leftMouseDown,.leftMouseUp,.rightMouseDown,.rightMouseUp,.otherMouseDown,.otherMouseUp:
                guard FlowSessionPostMouse(event,&sessionButtons)==0 else { failPosting();return }
            default: event.post(tap:.cgSessionEventTap)
            }
        } else { event.post(tap:.cghidEventTap) }
    }
    private func failPosting() {
        guard !injectionUnavailable else{return};injectionUnavailable=true
        inputLogger.error("stop-cause=input-posting-denied")
        DispatchQueue.main.async { [weak self] in
            guard let self else{return};self.enabled=false
            if let failure=self.injectionFailure { failure() } else { self.emergency?() }
        }
    }
    private func injectPointer(_ message: Message) {
        let r = bounds; lastPoint = CGPoint(x: r.minX + message.x! * (r.width-1), y: r.minY + message.y! * (r.height-1))
        let type: CGEventType = injectedButtons.contains(0) ? .leftMouseDragged : injectedButtons.contains(1) ? .rightMouseDragged : injectedButtons.isEmpty ? .mouseMoved : .otherMouseDragged
        if let event = CGEvent(mouseEventSource: eventSource, mouseType: type, mouseCursorPosition: lastPoint, mouseButton: CGMouseButton(rawValue: UInt32(injectedButtons.first ?? 0)) ?? .left) { post(event) }
    }
    private func injectScroll(dx: Int, dy: Int) {
        guard let event = CGEvent(scrollWheelEvent2Source: eventSource, units: .pixel, wheelCount: 2,
                                  wheel1: Int32(clamping: dy), wheel2: Int32(clamping: dx), wheel3: 0) else { return }
        // Mark the event continuous with integer point deltas so applications use the
        // precise pixel distance instead of rounding a line-based wheel event.
        event.setIntegerValueField(.scrollWheelEventIsContinuous, value: 1)
        event.setIntegerValueField(.scrollWheelEventPointDeltaAxis1, value: Int64(dy))
        event.setIntegerValueField(.scrollWheelEventPointDeltaAxis2, value: Int64(dx))
        post(event)
    }
    private func injectKey(_ hid: Int, down: Bool) {
        let repeated = down && injectedKeys.contains(hid)
        if down { injectedKeys.insert(hid) } else { injectedKeys.remove(hid) }
        keyEvent(hid, down: down, repeated: repeated)
    }
    private var modifierFlags: CGEventFlags {
        var flags: CGEventFlags = []
        // Injected key events must carry the shared Caps state; otherwise text goes
        // through lowercase even though the system/LED was switched on.
        if capsState { flags.insert(.maskAlphaShift) }
        for key in injectedKeys {
            if [224,228].contains(key) { flags.insert(.maskControl) }
            if [225,229].contains(key) { flags.insert(.maskShift) }
            if [226,230].contains(key) { flags.insert(.maskAlternate) }
            if [227,231].contains(key) { flags.insert(.maskCommand) }
        }
        return flags
    }
    private func keyEvent(_ hid: Int, down: Bool, repeated: Bool = false) {
        guard let code = KeyMap.byHID[hid] else { return }
        postKeyboard(code: code, down: down, modifier: (224...231).contains(hid), repeated: repeated)
    }
    private func injectButton(_ button: Int, down: Bool) {
        if down { injectedButtons.insert(button) } else { injectedButtons.remove(button) }
        buttonEvent(button, down: down)
    }
    private func buttonEvent(_ button: Int, down: Bool) {
        let type: CGEventType = button == 0 ? (down ? .leftMouseDown : .leftMouseUp) : button == 1 ? (down ? .rightMouseDown : .rightMouseUp) : (down ? .otherMouseDown : .otherMouseUp)
        if down {
            let now = ProcessInfo.processInfo.systemUptime
            let previous = clicks[button]
            let nearby = previous.map { hypot($0.point.x-lastPoint.x, $0.point.y-lastPoint.y) < 6 && now-$0.time <= NSEvent.doubleClickInterval } ?? false
            clicks[button] = (now, lastPoint, nearby ? (previous!.count+1) : 1)
        }
        if let e = CGEvent(mouseEventSource: eventSource, mouseType: type, mouseCursorPosition: lastPoint, mouseButton: CGMouseButton(rawValue: UInt32(button)) ?? .left) {
            e.setIntegerValueField(.mouseEventClickState, value: Int64(clicks[button]?.count ?? 1)); post(e)
        }
    }
    private func releaseInjected() {
        let keys = injectedKeys; injectedKeys.removeAll()
        for key in keys { keyEvent(key, down: false) }
        for button in injectedButtons { buttonEvent(button, down: false) }
        injectedButtons.removeAll()
    }
    func reset() {
        peerSupportsGameMode=false;gameShortcut=GameShortcut();onGameMode?(false,"off")
        cancelTouchpad();peerTouchpadEnabled=false;peerSupportsTouchpad=false;peerReceivesTouchpad=false;peerSupportsTouchpadFeedback=false
        stopFrameTimer(); clearFrameInput()
        releaseInjected(); router.reset(local: "mac")
        cursorCapture.end()
        physicalKeys.removeAll(); physicalButtons.removeAll(); clicks.removeAll(); onFocus?("mac")
    }
    func stop() {
        enabled = false
        if let source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap { CFMachPortInvalidate(tap) }
        source = nil; tap = nil
    }
    func finish() { stop();cursorCapture.finish() }
    deinit {
        FlowHIDClose(hidConnection)
        frameTimer?.cancel()
    }
}
