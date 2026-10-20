import AppKit
import Combine
import CryptoKit
import FlowCore
import OSLog

final class FlowModel: ObservableObject {
    private let lifecycleLogger=Logger(subsystem:"com.listfxflow.mac",category:"sharing-state")
    private var lastStopReason:String?
    @Published var connected = false
    @Published var listening = false
    @Published var autoStarting = false
    @Published var clipboardStatus = "剪贴板未连接"
    private let clipboard = ClipboardBridge()
    private var launchHandled = false
    private var bridgeTimer: Timer?
    private var bridgeRecovery=BridgeRecovery()
    private var preferredAddress:String?
    private var activeBridgeID:String?
    @Published var status = "连接雷雳线，开启一个共同的工作空间。"
    @Published var bridge = "等待雷雳连接"
    @Published var active = "mac"
    @Published var gameProtection=false
    @Published var gameProtectionAvailable=false
    @Published var gameProtectionStatus="更新并连接 Windows 后可用"
    @Published var side: Side = .right
    @Published var offset: Double = 0
    @Published var topology = Topology()
    @Published var touchpadEnabled=UserDefaults.standard.bool(forKey:"touchpadEnabled") {
        didSet { UserDefaults.standard.set(touchpadEnabled,forKey:"touchpadEnabled");input.touchpadEnabled=touchpadEnabled;updateTouchpadStatus() }
    }
    @Published var touchpadStatus="默认关闭 · 两端开启后生效"
    private var touchpadActivity:String?
    @Published var showingPermissions = false
    @Published var permissionHint = "分别开启下面两项权限，再返回应用。"
    let permissions = InputPermissions()
    private var permissionTimer: Timer?
    private let permissionDragPanel = PermissionDragPanel()
    private let server:FlowServer
    private let loginWindow:Bool
    private var peerClipboard=false
    private let input = InputEngine()
    private var observers: [NSObjectProtocol] = []
    private var distributedObservers: [NSObjectProtocol] = []
    private var resumeOnWake = false
    private var sessionActive = true { didSet { input.touchpadSessionActive=sessionActive } }
    private var sleepPending = false
    private var wakeGuardUntil = 0.0
    init(login:LoginConfiguration?=nil) {
        loginWindow=login != nil
        wakeGuardUntil=ProcessInfo.processInfo.systemUptime+8
        preferredAddress=login?.preferredAddress ?? UserDefaults.standard.string(forKey:"lastBridgeAddress")
        if let login {
            server=FlowServer(identityProvider:{ try P256.KeyAgreement.PrivateKey(rawRepresentation:login.identity) },loadPeer:{ login.peer },savePeer:{ peer in guard peer==login.peer else { throw FlowMacError.changedPeer } },requirePinnedPeer:true)
        } else { server=FlowServer() }
        side = login?.side ?? Side(rawValue: UserDefaults.standard.string(forKey: "side") ?? "right") ?? .right
        offset = login?.offset ?? UserDefaults.standard.double(forKey: "offset")
        sessionActive = !(CGSessionCopyCurrentDictionary().flatMap { ($0 as? [String:Any])?["CGSSessionScreenIsLocked"] as? Bool } ?? false)
        if !sessionActive { server.requireKnownPeer() }
        updateTopology()
        input.touchpadEnabled=touchpadEnabled;input.touchpadSessionActive=sessionActive
        input.send = { [weak self] in self?.server.send($0) }
        input.onTouchpadActivity = { [weak self] value in self?.touchpadActivity=value;self?.updateTouchpadStatus() }
        input.onGameMode = { [weak self] locked,phase in
            self?.gameProtection=locked
            self?.gameProtectionStatus=locked ? (phase=="automatic" ? "自动保护中 · 输入固定在 Windows":"已手动锁定 Windows"):"按 Ctrl + Option + L 锁定 Windows"
        }
        input.emergency = { [weak self] in self?.stop(reason: "紧急停止 · 已恢复本机控制") }
        input.onFocus = { [weak self] in self?.active = $0;self?.touchpadActivity=nil;self?.updateTouchpadStatus() }
        server.onAuthenticatedAddress = { [weak self] address in
            guard let self else{return}
            self.preferredAddress=address
            self.activeBridgeID=self.currentBridges.first { $0.address==address }?.id
            if !self.loginWindow { UserDefaults.standard.set(address,forKey:"lastBridgeAddress") }
            self.refreshBridge()
        }
        server.onStatus = { [weak self] connected, status in
            guard let self else { return }
            self.connected = connected
            self.status = !connected && !self.listening ? (self.lastStopReason ?? status):status
            self.input.enabled = connected
            if !connected { self.touchpadActivity=nil;self.gameProtection=false;self.gameProtectionAvailable=false;self.gameProtectionStatus="连接 Windows 后可用" }
            self.updateTouchpadStatus()
            if !connected { self.clipboard.stop();self.activeBridgeID=nil }
            if !connected && !self.server.isListening && self.listening {
                self.listening = false;self.input.stop()
                self.bridgeRecovery.failed(now:ProcessInfo.processInfo.systemUptime)
                self.autoStarting=self.bridgeRecovery.requested
            }
            if connected {
                self.showingPermissions = false
                self.announceDisplay(); self.publishLayout(); self.publishLocalPointer()
            }
        }
        server.onMessage = { [weak self] message in self?.receive(message) }
        let center = NSWorkspace.shared.notificationCenter
        // Display sleep does not interrupt sharing: the network and event tap stay
        // alive while the Mac is still awake. The power assertion below keeps an
        // unattended Mac from idle-sleeping in the first place.
        observers.append(center.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in self?.sleepStop() })
        observers.append(center.addObserver(forName: NSWorkspace.sessionDidResignActiveNotification, object: nil, queue: .main) { [weak self] _ in
            guard let self else { return }
            self.sessionActive = false
            if !self.loginWindow && !self.isConsoleSession {
                let resumable=self.resumeOnWake || self.listening || self.connected || self.autoStarting
                self.stop(reason:self.status,resumable:resumable)
                return
            }
            if LoginSupport.installed || self.loginWindow { self.server.requireKnownPeer();self.clipboard.stop();return }
            let resumable = self.resumeOnWake || self.listening || self.connected || self.autoStarting
            if !resumable { self.resumeOnWake = false }
            self.stop(reason: resumable ? "Mac 已锁定 · 解锁后自动恢复共享" : "Mac 已锁定 · 共享已停止",
                      resumable: resumable)
        })
        observers.append(center.addObserver(forName: NSWorkspace.sessionDidBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            self?.handleUnlock()
        })
        // Supplement the documented workspace notifications: screen locking does not always switch sessions.
        // This distributed notification is advisory; lock/secure-input behavior still needs hardware acceptance.
        distributedObservers.append(DistributedNotificationCenter.default().addObserver(forName: NSNotification.Name("com.apple.screenIsLocked"), object: nil, queue: .main) { [weak self] _ in
            guard let self else { return }
            self.sessionActive = false
            if LoginSupport.installed || self.loginWindow { self.server.requireKnownPeer();self.clipboard.stop();return }
            let resumable = self.resumeOnWake || self.listening || self.connected || self.autoStarting
            if !resumable { self.resumeOnWake = false }
            self.stop(reason: resumable ? "Mac 已锁定 · 解锁后自动恢复共享" : "Mac 已锁定 · 共享已停止",
                      resumable: resumable)
        })
        distributedObservers.append(DistributedNotificationCenter.default().addObserver(forName: NSNotification.Name("com.apple.screenIsUnlocked"), object: nil, queue: .main) { [weak self] _ in
            self?.handleUnlock()
        })
        observers.append(center.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in self?.handleWake() })
        observers.append(center.addObserver(forName: NSWorkspace.screensDidWakeNotification, object: nil, queue: .main) { [weak self] _ in self?.handleWake() })
        observers.append(NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            guard let self else { return }
            if self.autoStarting || self.resumeOnWake || ProcessInfo.processInfo.systemUptime < self.wakeGuardUntil {
                self.updateTopology()
                if self.listening { self.announceDisplay(); self.publishLayout() }
                return
            }
            self.stop(reason: "显示器配置已变化，请重新启用共享。")
            self.updateTopology()
        })
        observers.append(NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in self?.permissions.refresh() })
        refreshBridge()
        let timer=Timer(timeInterval:1,repeats:true) { [weak self] _ in self?.checkBridge() }
        RunLoop.main.add(timer,forMode:.common);bridgeTimer=timer
        clipboard.onStatus = { [weak self] in self?.clipboardStatus = $0 }
    }
    private var currentBridges:[BridgeAddress] {
        let all=BridgeAddress.all()
        let order=AddressPreference.ordered(all.map(\.address),preferred:preferredAddress)
        return order.compactMap { ip in all.first { $0.address==ip } }
    }
    func refreshBridge() { let addresses = currentBridges; bridge = addresses.isEmpty ? "等待雷雳网桥分配地址" : "bridge0 · " + addresses.map(\.address).joined(separator: " / ") }
    func startOnLaunch() {
        guard !launchHandled else { return }; launchHandled = true
        start()
    }
    private func checkBridge() {
        let addresses=currentBridges
        refreshBridge()
        switch bridgeRecovery.action(addresses:Set(addresses.map(\.id)),listening:listening,now:ProcessInfo.processInfo.systemUptime,activeAddress:connected ? activeBridgeID:nil) {
        case .none: break
        case .waitForAddress:
            if listening { server.stop();input.stop();clipboard.stop();connected=false;listening=false }
            autoStarting=true
            if !showingPermissions { status="自动共享已开启，等待雷雳网桥…" }
        case .start:
            if listening { server.stop();input.stop();clipboard.stop();connected=false;listening=false }
            start()
        }
    }
    func start() {
        bridgeRecovery.request()
        guard !listening else { return }
        lastStopReason=nil
        autoStarting = true
        permissions.refresh()
        guard permissions.status.ready else {
            permissionHint = "分别开启下面两项权限，再返回应用。"
            showingPermissions = true; status = "需要授权辅助功能和输入监控。"; return
        }
        do {
            refreshBridge()
            let addresses = currentBridges
            guard !addresses.isEmpty else {
                status = "自动共享已开启，等待雷雳网桥…"; showingPermissions = false; return
            }
            try input.start(); try server.start(bridges: addresses); listening = true
            bridgeRecovery.started(addresses:Set(addresses.map(\.id)))
            autoStarting = false
            showingPermissions = false
        } catch {
            server.stop();input.stop();clipboard.stop();connected=false;listening=false
            bridgeRecovery.failed(now:ProcessInfo.processInfo.systemUptime)
            autoStarting=true;status=error.localizedDescription
            if case FlowMacError.permission = error {
                permissionHint = "系统尚未允许建立输入连接。若开关已开启，请重新打开应用后再试。"
                showingPermissions = true
            }
        }
    }
    func watchPermissions() {
        permissions.refresh(); permissionTimer?.invalidate()
        permissionTimer = Timer.scheduledTimer(withTimeInterval: 0.75, repeats: true) { [weak self] _ in self?.permissions.refresh() }
    }
    func stopWatchingPermissions() { permissionTimer?.invalidate(); permissionTimer = nil; permissionDragPanel.close() }
    func openPermission(_ permission: InputPermission) {
        permissions.request(permission)
        let pane = permission == .accessibility ? "Privacy_Accessibility" : "Privacy_ListenEvent"
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)") {
            if !NSWorkspace.shared.open(url) { permissionHint = "请手动打开系统设置的「\(permission == .accessibility ? "辅助功能" : "输入监控")」页面。" }
        }
        permissionDragPanel.show(for: permission)
    }
    func revealApplication() { NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL]) }
    func reopen() {
        stop()
        let config = NSWorkspace.OpenConfiguration(); config.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: config) { _, error in
            DispatchQueue.main.async {
                if let error { self.permissionHint = "重新打开失败：\(error.localizedDescription)" }
                else { NSApp.terminate(nil) }
            }
        }
    }
    func stop(reason: String = "共享已停止 · 本机控制", resumable: Bool = false) {
        lifecycleLogger.notice("stop reason=\(reason,privacy:.public) resumable=\(resumable,privacy:.public) connected=\(self.connected,privacy:.public) listening=\(self.listening,privacy:.public)")
        lastStopReason=reason
        resumeOnWake = resumable
        if !resumable { sleepPending = false }
        autoStarting = false;bridgeRecovery.stop()
        server.stop(); input.stop(); connected = false; listening = false; status = reason
    }
    func finish() { stop();input.finish() }
    private func sleepStop() {
        // Only sleep/display-sleep should auto-resume. A manual stop, lock, remote
        // reset, or emergency stop remains stopped until the user starts it again.
        sleepPending = true
        let resumable = resumeOnWake || bridgeRecovery.requested
        stop(reason: resumable ? "系统已休眠 · 唤醒后自动恢复共享" : "系统已休眠 · 共享已停止", resumable: resumable)
    }
    private func handleWake() {
        refreshBridge()
        wakeGuardUntil = ProcessInfo.processInfo.systemUptime + 8
        let locked = CGSessionCopyCurrentDictionary()
            .flatMap { ($0 as? [String: Any])?["CGSSessionScreenIsLocked" as String] as? Bool } ?? false
        sessionActive = !locked
        resumeAfterWake()
    }
    private func resumeAfterWake() {
        guard resumeOnWake, (loginWindow || isConsoleSession), (sessionActive || LoginSupport.installed || loginWindow), !listening else { return }
        resumeOnWake = false; sleepPending = false
        start()
    }
    private func handleUnlock() {
        guard loginWindow || isConsoleSession else { return }
        let changed = !sessionActive
        sessionActive=true;resumeAfterWake()
        if changed { startClipboardIfAllowed() }
    }
    private var isConsoleSession:Bool {
        (CGSessionCopyCurrentDictionary() as? [String:Any])?[kCGSessionOnConsoleKey as String] as? Bool ?? false
    }
    func arrange(_ side: Side, offset: Double = 0) {
        self.side = side; self.offset = min(900, max(-900, offset))
        if !loginWindow { UserDefaults.standard.set(side.rawValue, forKey: "side"); UserDefaults.standard.set(self.offset, forKey: "offset") }
        updateTopology(); publishLayout()
    }
    private func updateTopology() {
        let r = input.bounds
        topology.side = side; topology.offset = offset; topology.macAspect = r.width / max(1,r.height)
        input.router.updateTopology(topology)
    }
    private func publishLayout() { var m = Message("layout"); m.side = side.rawValue; m.offset = offset; server.send(m) }
    private func updateTouchpadStatus() {
        if !touchpadEnabled { touchpadStatus="默认关闭 · 两端开启后生效" }
        else if !connected { touchpadStatus="等待连接远端" }
        else if !input.peerSupportsTouchpad { touchpadStatus="远端版本不支持，请更新两端应用。" }
        else if !input.peerTouchpadEnabled { touchpadStatus="等待远端开启触控板手势映射" }
        else if !input.peerReceivesTouchpad { touchpadStatus="远端系统暂不支持接收原生手势；仍可向本机转发。" }
        else { touchpadStatus=touchpadActivity ?? "已就绪 · 等待远端手势触点" }
    }
    private func announceDisplay() { let r = input.bounds;guard r.width>1,r.height>1 else{return};var m = Message("display"); m.width = r.width; m.height = r.height; m.features = loginWindow ? ["caps-v1"] : TouchpadAvailability.displayFeatures; server.send(m) }
    private func publishLocalPointer() {
        let r = input.bounds, p = CGEvent(source: nil)?.location ?? r.origin
        var m = Message("pointer"); m.active = "mac"; m.x = min(1,max(0,(p.x-r.minX)/r.width)); m.y = min(1,max(0,(p.y-r.minY)/r.height))
        input.receive(m); server.send(m)
    }
    private func receive(_ message: Message) {
        switch message.type {
        case "display":
            input.peerSupportsGameMode=message.features?.contains("game-mode-v1")==true
            gameProtectionAvailable=input.peerSupportsGameMode
            gameProtectionStatus=gameProtectionAvailable ? "按 Ctrl + Option + L 锁定 Windows":"更新 Windows 后可用"
            topology.windowsAspect = message.width! / message.height!
            updateTopology(); publishLayout()
            let capsSync = message.features?.contains("caps-v1") == true
            input.peerSupportsTouchpad=message.features?.contains("touchpad-v1")==true
            input.peerReceivesTouchpad=message.features?.contains("touchpad-native-receive-v1")==true
            input.peerSupportsTouchpadFeedback=message.features?.contains("touchpad-feedback-v1")==true
            input.sendTouchpadSetting();updateTouchpadStatus()
            input.peerSupportsCaps = capsSync
            if capsSync { input.sendCapsState() }
            peerClipboard=message.features?.contains("clipboard-v1") == true
            startClipboardIfAllowed()
        case "touchpadControl": input.receive(message);updateTouchpadStatus()
        case "layout": arrange(Side(rawValue: message.side!)!, offset: message.offset!)
        case "reset": stop(reason: "Windows 已停止共享 · 已恢复本机控制")
        default: input.receive(message)
        }
    }
    func toggleGameProtection() { input.toggleGameProtection() }
    private func startClipboardIfAllowed() {
        guard connected,peerClipboard,sessionActive,!loginWindow else{return}
        do { try clipboard.start(bridges:currentBridges) { [weak self] token in
            var ready=Message("clipboardReady");ready.nonce=token.hex;self?.server.send(ready)
        } } catch { clipboardStatus=error.localizedDescription }
    }
    deinit {
        bridgeTimer?.invalidate()
        permissionTimer?.invalidate()
        for observer in observers { NSWorkspace.shared.notificationCenter.removeObserver(observer); NotificationCenter.default.removeObserver(observer) }
        for observer in distributedObservers { DistributedNotificationCenter.default().removeObserver(observer) }
    }
}
