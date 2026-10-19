import AppKit
import FlowHID

/// WindowServer calls never run inside the raw input callback. Ownership, cursor
/// visibility and all repairs are serialized on one queue.
final class CursorCapture {
    struct Driver {
        var allowBackground: () -> Bool
        var associate: (Bool) -> Bool
        var hide: () -> Bool
        var show: () -> Void
        var position: () -> CGPoint?
        var warp: (CGPoint) -> Bool
        static let system = Driver(
            allowBackground: { FlowEnableBackgroundCursorControl() },
            associate: { CGAssociateMouseAndMouseCursorPosition($0 ? 1 : 0) == .success },
            hide: { CGDisplayHideCursor(CGMainDisplayID()) == .success },
            show: { _ = CGDisplayShowCursor(CGMainDisplayID()) },
            position: { CGEvent(source:nil)?.location },
            warp: { CGWarpMouseCursorPosition($0) == .success })
    }
    private let queue: DispatchQueue
    private let driver: Driver
    private let automaticPolling: Bool
    private var active=false,detached=false,hidden=false
    private var anchor=CGPoint.zero
    private var pinTimer:DispatchSourceTimer?
    private var failures=0
    private var generation:UInt64=0
    // Only this small flag crosses from the input callback to the parking queue.
    private let motionLock=NSLock()
    private var motionPending=false
    var onLost:(()->Void)?
    init(driver:Driver = .system,
         queue:DispatchQueue=DispatchQueue(label:"ListFXFlow.cursor.parking",qos:.userInteractive),
         automaticPolling:Bool=true) {
        self.driver=driver;self.queue=queue;self.automaticPolling=automaticPolling
    }
    func begin(at point:CGPoint) {
        queue.async { [weak self] in
            guard let self else{return}
            self.anchor=point
            guard !self.active else { self.repair();return }
            self.active=true;self.generation &+= 1;self.failures=0
            self.repair()
            guard self.automaticPolling else{return}
            let timer=DispatchSource.makeTimerSource(queue:self.queue)
            timer.schedule(deadline:.now() + .milliseconds(8),repeating:.milliseconds(8),leeway:.milliseconds(1))
            timer.setEventHandler { [weak self] in self?.poll() }
            self.pinTimer=timer;timer.resume()
        }
    }
    private func repair() {
        _ = driver.allowBackground()
        // Background control must be enabled before hiding/detaching. A successful
        // associate() result alone does not prove it remains effective in background.
        if !hidden { hidden=driver.hide() }
        detached=driver.associate(false) || detached
        park()
    }
    // Called on the parking queue, independently of the event-tap/main run loop.
    func poll() {
        guard active else{return}
        motionLock.lock();let moved=motionPending;motionPending=false;motionLock.unlock()
        let p=driver.position()
        let drift=p.map { hypot($0.x-anchor.x,$0.y-anchor.y)>0.25 } ?? true
        if drift || !detached || !hidden { repair() }
        // Never rely only on a position snapshot: it may precede the HID sample.
        // One unconditional warp per moving 8 ms slot also covers that race.
        else if moved { park() }
    }
    private func park() {
        if driver.warp(anchor) { failures=0 }
        else {
            failures+=1
            if failures==3 {
                let token=generation
                DispatchQueue.main.async { [weak self] in
                    guard let self,self.queue.sync(execute:{ self.active && self.generation==token }) else{return}
                    self.onLost?()
                }
            }
        }
    }
    func pinIfNeeded() {
        motionLock.lock();motionPending=true;motionLock.unlock()
    }
    func end(at point:CGPoint?=nil) {
        queue.async { [weak self] in self?.release(at:point) }
    }
    private func release(at point:CGPoint?=nil) {
        guard active else{return}
        active=false;generation &+= 1;pinTimer?.cancel();pinTimer=nil
        motionLock.lock();motionPending=false;motionLock.unlock()
        // Ordered after old watchdog warps, before showing the pointer.
        if detached { _ = driver.associate(true);detached=false }
        if let point { _ = driver.warp(point) }
        if hidden { driver.show();hidden=false }
    }
    func finish() { queue.sync { release() } }
    deinit {
        pinTimer?.cancel()
        if detached { _ = driver.associate(true) }
        if hidden { driver.show() }
    }
}
