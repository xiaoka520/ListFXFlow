import AppKit
import CryptoKit
import Darwin
import FlowCore
import OSLog
import SystemConfiguration

enum LoginContext {
    static var console:(name:String?,uid:UInt32) {
        var uid:uid_t=0,gid:gid_t=0
        return (SCDynamicStoreCopyConsoleUser(nil,&uid,&gid) as String?,uid)
    }
    static var loggedIn:Bool { let value=console;return LoginSession.isLoggedIn(name:value.name,uid:value.uid) }
    static var manager:String {
        let process=Process(),pipe=Pipe();process.executableURL=URL(fileURLWithPath:"/bin/launchctl")
        process.arguments=["managername"];process.standardOutput=pipe;process.standardError=FileHandle.nullDevice
        do {
            try process.run()
            let deadline=ProcessInfo.processInfo.systemUptime+2
            while process.isRunning && ProcessInfo.processInfo.systemUptime<deadline { Thread.sleep(forTimeInterval:0.01) }
            if process.isRunning { process.terminate();return "unknown" }
            let data=pipe.fileHandleForReading.readDataToEndOfFile()
            guard process.terminationStatus==0,data.count<256 else{return "unknown"}
            return String(decoding:data,as:UTF8.self).trimmingCharacters(in:.whitespacesAndNewlines)
        } catch { return "unknown" }
    }
}

enum LoginStatusFile {
    static let path=LoginSupport.root+"/login-state.json"
    static func write(_ state:LoginStatus) throws {
        var directory=stat()
        guard geteuid()==0,lstat(LoginSupport.root,&directory)==0,directory.st_uid==0,
              directory.st_mode & S_IFMT == S_IFDIR,directory.st_mode & 0o022 == 0,state.valid else { throw FlowMacError.permission }
        try JSONEncoder().encode(state).write(to:URL(fileURLWithPath:path),options:.atomic)
        guard chmod(path,0o644)==0 else{throw POSIXError(.EACCES)}
    }
    static func read()->LoginStatus? {
        var directory=stat()
        guard lstat(LoginSupport.root,&directory)==0,directory.st_uid==0,directory.st_mode & S_IFMT == S_IFDIR,directory.st_mode & 0o022==0 else{return nil}
        let fd=open(path,O_RDONLY|O_NOFOLLOW|O_CLOEXEC);guard fd>=0 else{return nil};defer{close(fd)}
        var info=stat()
        guard fstat(fd,&info)==0,info.st_uid==0,info.st_mode & S_IFMT == S_IFREG,info.st_mode & 0o022==0,info.st_size>0,info.st_size<=8192 else{return nil}
        var bytes=[UInt8](repeating:0,count:Int(info.st_size))
        guard Darwin.read(fd,&bytes,bytes.count)==bytes.count,let value=try? JSONDecoder().decode(LoginStatus.self,from:Data(bytes)),value.valid else{return nil}
        return value
    }
    static var description:String {
        guard let state=read() else{return "尚无登录窗口运行记录。"}
        let recorded=ISO8601DateFormatter().string(from:state.recordedAt)
        var text="记录时间：\(recorded)\n进程：\(state.pid)\n阶段：\(state.phase.rawValue)\n\(state.detail)"
        if let previous=state.previousDetail { text += "\n退出前：\(previous)" }
        return text
    }
}

/// Separate from the desktop model: display initialization cannot cancel startup,
/// and no pasteboard, permission panel or user-window model is constructed here.
final class PreLoginService:NSObject,NSApplicationDelegate {
    private let logger=Logger(subsystem:"com.listfxflow.mac",category:"loginwindow")
    private var state=LoginStatus(phase:.starting,detail:"登录窗口代理启动",uptime:ProcessInfo.processInfo.systemUptime,pid:getpid())
    private var input:InputEngine?
    private var server:FlowServer?
    private var configuration:LoginConfiguration?
    private var recovery=BridgeRecovery()
    private var timer:Timer?,sessionTimer:Timer?
    private var inputStarted=false,listenerReady=false,connected=false,finishing=false,refreshing=false
    private var activeAddress:String?
    private var topology=Topology()
    private var lastBounds=CGRect.zero
    private var displayObserver:NSObjectProtocol?
    private var watchdog:DispatchSourceTimer?

    func applicationDidFinishLaunching(_ notification:Notification) {
        guard geteuid()==0,!LoginContext.loggedIn else{NSApp.terminate(nil);return}
        do {
            let config=try LoginSupport.load();configuration=config;topology.side=config.side;topology.offset=config.offset
            let engine=InputEngine(loginWindow:true)
            let transport=FlowServer(identityProvider:{try P256.KeyAgreement.PrivateKey(rawRepresentation:config.identity)},loadPeer:{config.peer},savePeer:{peer in guard peer==config.peer else{throw FlowMacError.changedPeer}},requirePinnedPeer:true)
            input=engine;server=transport
            engine.send={ [weak self] message in guard !LoginContext.loggedIn else{return};self?.server?.send(message) }
            engine.emergency={ [weak self] in self?.pause() }
            engine.injectionFailure={ [weak self] in self?.fail("登录窗口拒绝输入，等待权限或会话恢复") }
            transport.onMessage={ [weak self] message in self?.receive(message) }
            transport.onAuthenticatedAddress={ [weak self] address in self?.activeAddress=address }
            transport.onListeningReady={ [weak self] in
                guard let self,!self.finishing,self.inputStarted,self.recovery.requested else{return}
                self.listenerReady=true
                if !self.connected { self.publish(.listening,"雷雳监听已就绪，等待已配对的 Windows") }
            }
            transport.onStatus={ [weak self] ready,reason in self?.connectionChanged(ready,reason:reason) }
            displayObserver=NotificationCenter.default.addObserver(forName:NSApplication.didChangeScreenParametersNotification,object:nil,queue:.main) { [weak self] _ in
                // ThunderDisplay-style readiness retry, never the desktop Stop action.
                self?.refresh()
            }
            recovery.request()
            let health=Timer(timeInterval:1,repeats:true) { [weak self] _ in self?.refresh() }
            RunLoop.main.add(health,forMode:.common);timer=health
            let session=Timer(timeInterval:0.1,repeats:true) { [weak self] _ in
                guard let self,!self.finishing,LoginContext.loggedIn else{return};self.handOver()
            }
            RunLoop.main.add(session,forMode:.common);sessionTimer=session
            refresh()
        } catch { publish(.blocked,"登录组件配置不可用，请更新组件");NSApp.terminate(nil) }
    }
    private func publish(_ phase:LoginStatus.Phase,_ detail:String) {
        if state.phase != phase || state.detail != detail { logger.notice("phase=\(phase.rawValue,privacy:.public); \(detail,privacy:.public)") }
        state.update(phase,detail:detail,uptime:ProcessInfo.processInfo.systemUptime)
        do { try LoginStatusFile.write(state) } catch { logger.error("无法写入登录状态心跳") }
    }
    private func refresh() {
        guard !finishing,!refreshing else{return}
        refreshing=true;defer{refreshing=false}
        guard !LoginContext.loggedIn else{handOver();return}
        guard recovery.requested,let input,let server else{publish(state.phase,state.detail);return}
        let available=BridgeAddress.all()
        let order=AddressPreference.ordered(available.map(\.address),preferred:configuration?.preferredAddress)
        let bridges=order.compactMap { ip in available.first { $0.address==ip } }
        guard !bridges.isEmpty else {
            if inputStarted { suspend() }
            publish(.waitingBridge,"等待雷雳网桥 IPv4 地址，保持自动重试");return
        }
        if !connected { armWatchdog() }
        defer{disarmWatchdog()}
        let bounds=input.bounds
        guard (320...32768).contains(bounds.width),(200...32768).contains(bounds.height) else {
            if inputStarted { suspend() }
            publish(.waitingDisplay,"等待登录显示器就绪，保持自动重试");return
        }
        let changed=bounds != lastBounds
        if changed {
            lastBounds=bounds;topology.macAspect=bounds.width/bounds.height;input.router.updateTopology(topology)
            if connected { announceDisplay() }
        }
        let activeID=connected ? bridges.first(where:{$0.address==activeAddress})?.id:nil
        switch recovery.action(addresses:Set(bridges.map(\.id)),listening:inputStarted && server.isListening,now:ProcessInfo.processInfo.systemUptime,activeAddress:activeID) {
        case .none: publish(state.phase,state.detail)
        case .waitForAddress: break
        case .start:
            suspend()
            publish(.checking,"检查登录窗口输入权限并启动雷雳监听")
            do {
                try input.start();inputStarted=true
                try server.start(bridges:bridges)
                recovery.started(addresses:Set(bridges.map(\.id)))
            } catch { fail(error.localizedDescription) }
        }
    }
    private func connectionChanged(_ ready:Bool,reason:String) {
        guard !finishing,recovery.requested,inputStarted,let input,let server else{return}
        guard !LoginContext.loggedIn else{handOver();return}
        connected=ready;input.enabled=ready
        if ready {
            publish(.connected,"已连接配对设备，登录窗口键鼠共享中")
            announceDisplay()
            let rect=topology.mac,position=input.router.position
            var point=Message("pointer");point.active="mac";point.x=min(1,max(0,(position.x-rect.x)/rect.width));point.y=min(1,max(0,(position.y-rect.y)/rect.height))
            server.send(point)
        } else if server.isListening {
            activeAddress=nil
            if listenerReady { publish(.listening,"雷雳监听已就绪，等待已配对的 Windows") }
        } else { fail(reason) }
    }
    private func receive(_ message:Message) {
        guard !finishing,!LoginContext.loggedIn,connected,let input else{return}
        switch message.type {
        case "display":
            topology.windowsAspect=message.width!/message.height!;input.router.updateTopology(topology)
            input.peerSupportsCaps=message.features?.contains("caps-v1")==true
            if input.peerSupportsCaps { input.sendCapsState() }
            announceDisplay()
        case "layout":
            topology.side=Side(rawValue:message.side!)!;topology.offset=message.offset!;input.router.updateTopology(topology)
            var layout=Message("layout");layout.side=topology.side.rawValue;layout.offset=topology.offset;server?.send(layout)
        case "reset": pause()
        default: input.receive(message)
        }
    }
    private func announceDisplay() {
        guard let bounds=input?.bounds,(320...32768).contains(bounds.width),(200...32768).contains(bounds.height) else{return}
        var message=Message("display");message.width=bounds.width;message.height=bounds.height;message.features=["caps-v1"];server?.send(message)
        var layout=Message("layout");layout.side=topology.side.rawValue;layout.offset=topology.offset;server?.send(layout)
    }
    private func suspend() {
        inputStarted=false;listenerReady=false;connected=false;activeAddress=nil
        input?.stop();server?.stop()
    }
    private func fail(_ reason:String) {
        suspend();recovery.failed(now:ProcessInfo.processInfo.systemUptime)
        publish(.blocked,reason)
    }
    private func pause() {
        recovery.stop();suspend();publish(.blocked,"共享已由本机或远端停止，登录后可重新启用")
    }
    private func handOver() {
        guard !finishing else{return}
        finishing=true;timer?.invalidate();sessionTimer?.invalidate();disarmWatchdog()
        recovery.stop();suspend();input?.finish()
        publish(.stopped,"用户已登录，释放登录窗口输入与端口，由桌面应用接管")
        let console=LoginContext.console
        if console.uid==configuration?.desktopUID {
            let process=Process();process.executableURL=URL(fileURLWithPath:"/bin/launchctl")
            process.arguments=["kickstart","gui/\(console.uid)/com.listfxflow.autostart"]
            process.standardOutput=FileHandle.nullDevice;process.standardError=FileHandle.nullDevice
            try? process.run()
        }
        NSApp.terminate(nil)
    }
    func applicationWillTerminate(_ notification:Notification) {
        timer?.invalidate();sessionTimer?.invalidate();disarmWatchdog();finishing=true
        recovery.stop();suspend();input?.finish()
        publish(.stopped,"登录窗口代理退出")
        if let displayObserver { NotificationCenter.default.removeObserver(displayObserver) }
    }
    private func armWatchdog() {
        guard watchdog==nil else{return}
        let timer=DispatchSource.makeTimerSource(queue:DispatchQueue.global(qos:.utility))
        timer.schedule(deadline:.now()+15)
        timer.setEventHandler {
            let state=LoginStatus(phase:.blocked,detail:"登录窗口初始化超时，重启代理后重试",uptime:ProcessInfo.processInfo.systemUptime,pid:getpid())
            try? LoginStatusFile.write(state);_exit(1)
        }
        watchdog=timer;timer.resume()
    }
    private func disarmWatchdog() { watchdog?.cancel();watchdog=nil }
}
