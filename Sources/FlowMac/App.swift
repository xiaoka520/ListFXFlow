import SwiftUI
import FlowCore
import FlowHID
import Combine
import Darwin

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let model = FlowModel()
    private var mainWindow: NSWindow?
    private var statusController: StatusItemController?
    private var permissionsObserver: AnyCancellable?
    private var showObserver: NSObjectProtocol?
    func applicationDidFinishLaunching(_ notification: Notification) {
        installMenu()
        buildWindow()
        statusController = StatusItemController(model: model, show: { [weak self] in self?.showMainWindow() })
        permissionsObserver = model.$showingPermissions.removeDuplicates().sink { [weak self] visible in
            if visible { self?.showMainWindow() }
        }
        showObserver = DistributedNotificationCenter.default().addObserver(forName: SingleInstance.showNotification, object: nil, queue: .main) { [weak self] _ in
            self?.showMainWindow()
        }
        if !CommandLine.arguments.contains("--no-autostart") { model.startOnLaunch() }
        if CommandLine.arguments.contains("--show") { showMainWindow() }
    }
    private func installMenu() {
        let menu=NSMenu(), appMenu=NSMenu(), appItem=NSMenuItem(); appItem.submenu=appMenu; menu.addItem(appItem)
        appMenu.addItem(withTitle: "关于 ListFXFlow",action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)),keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "隐藏 ListFXFlow",action: #selector(NSApplication.hide(_:)),keyEquivalent: "h")
        appMenu.addItem(.separator()); appMenu.addItem(withTitle: "退出 ListFXFlow",action: #selector(NSApplication.terminate(_:)),keyEquivalent: "q")
        let edit=NSMenu(title: "编辑"), editItem=NSMenuItem(title: "编辑",action: nil,keyEquivalent: ""); editItem.submenu=edit; menu.addItem(editItem)
        for (title,action,key) in [("撤销","undo:","z"),("剪切","cut:","x"),("复制","copy:","c"),("粘贴","paste:","v"),("全选","selectAll:","a")] { edit.addItem(withTitle: title,action: NSSelectorFromString(action),keyEquivalent: key) }
        let windowMenu=NSMenu(title: "窗口"), windowItem=NSMenuItem(title: "窗口",action: nil,keyEquivalent: ""); windowItem.submenu=windowMenu; menu.addItem(windowItem)
        let show=NSMenuItem(title: "打开主界面",action: #selector(showMainWindow),keyEquivalent: "o"); show.target=self; windowMenu.addItem(show)
        windowMenu.addItem(withTitle: "隐藏到状态栏",action: #selector(NSWindow.performClose(_:)),keyEquivalent: "w")
        NSApp.mainMenu=menu; NSApp.windowsMenu=windowMenu
    }
    private func buildWindow() {
        let window = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 1060,height: 760),styleMask: [.titled,.closable,.miniaturizable,.resizable,.fullSizeContentView],backing: .buffered,defer: false)
        window.title = "ListFXFlow"; window.titleVisibility = .hidden; window.titlebarAppearsTransparent = true
        window.isReleasedWhenClosed = false; window.delegate = self; window.minSize = NSSize(width: 960,height: 710)
        window.contentViewController = NSHostingController(rootView: WorkspaceView(model: model).preferredColorScheme(.dark))
        window.center(); mainWindow = window
    }
    @objc func showMainWindow() {
        guard let window = mainWindow else { return }
        if window.isMiniaturized { window.deminiaturize(nil) }
        window.makeKeyAndOrderFront(nil); NSApp.activate()
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { sender.orderOut(nil); return false }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationShouldHandleReopen(_ sender: NSApplication,hasVisibleWindows flag: Bool) -> Bool { showMainWindow(); return false }
    func applicationWillTerminate(_ notification: Notification) {
        if let showObserver { DistributedNotificationCenter.default().removeObserver(showObserver) }
        model.finish()
    }
}

@main enum FlowApp {
    static func main() {
        if CommandLine.arguments.contains("--virtual-touchpad-status") { print(VirtualTouchpadCapability.description());return }
        if CommandLine.arguments.contains("--transport-self-test") { TransportSelfTest.run() }
        if CommandLine.arguments.contains("--login-status") { print(LoginStatusFile.description);return }
        if CommandLine.arguments.contains("--login-runtime-check") {
            let present=FlowPreLoginMarkerPresent()
            print("Pre-login marker: \(present); uid=\(geteuid()); manager=\(LoginContext.manager)")
            exit(present ? 0:1)
        }
        if let index=CommandLine.arguments.firstIndex(of:"--startup-user") {
            guard CommandLine.arguments.count>index+1,UInt32(CommandLine.arguments[index+1])==geteuid() else{return}
        }
        if let index=CommandLine.arguments.firstIndex(of:"--provision-login-support") {
            do { guard CommandLine.arguments.count>index+1,let uid=UInt32(CommandLine.arguments[index+1]) else { throw FlowMacError.protocolError };try LoginSupport.provision(owner:uid);exit(0) }
            catch { fputs("ListFXFlow login support provisioning failed\n",stderr);exit(1) }
        }
        if CommandLine.arguments.contains("--login-window") {
            guard geteuid()==0,LoginContext.manager=="LoginWindow" else {
                fputs("ListFXFlow login support: requires root LoginWindow context\n",stderr);exit(1)
            }
            guard !LoginContext.loggedIn else{return}
            let app=NSApplication.shared,delegate=PreLoginService();app.delegate=delegate;app.setActivationPolicy(.prohibited)
            let termination=terminationSignal()
            withExtendedLifetime((delegate,termination)) { app.run() };return
        }
        let instance = SingleInstance()
        guard instance.acquire() else { return }
        let app = NSApplication.shared
        let delegate = AppDelegate(); app.delegate = delegate
        app.setActivationPolicy(.accessory)
        let termination=terminationSignal()
        withExtendedLifetime((delegate, instance, termination)) { app.run() }
    }
    private static func terminationSignal()->DispatchSourceSignal {
        signal(SIGTERM,SIG_IGN)
        let source=DispatchSource.makeSignalSource(signal:SIGTERM,queue:.main)
        source.setEventHandler { NSApp.terminate(nil) };source.resume();return source
    }
}

private let accent = Color(red: 0.57, green: 0.55, blue: 1)
private let surface = Color(red: 0.075, green: 0.085, blue: 0.12)

struct WorkspaceView: View {
    @ObservedObject var model: FlowModel
    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 32) {
                HStack(spacing: 10) {
                    Image(systemName: "cursorarrow.motionlines").font(.system(size: 23, weight: .semibold)).foregroundStyle(accent)
                    Text("ListFXFlow").font(.system(size: 20, weight: .bold, design: .rounded))
                }.padding(.top, 24)
                VStack(alignment: .leading, spacing: 12) {
                    Label("工作空间", systemImage: "rectangle.2.swap").fontWeight(.semibold)
                        .padding(13).frame(maxWidth: .infinity, alignment: .leading)
                        .background(accent.opacity(0.13), in: RoundedRectangle(cornerRadius: 12)).foregroundStyle(accent)
                    Label("雷雳直连", systemImage: "bolt.horizontal.circle").foregroundStyle(.secondary).padding(.horizontal, 13)
                }
                Spacer()
                VStack(alignment: .leading, spacing: 10) {
                    Label("让设备之间，没有边界。", systemImage: "sparkle").font(.system(size: 11)).foregroundStyle(.secondary)
                    Text("MAC + WINDOWS\nVERSION \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev") · BUILD \(Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "dev")").font(.system(size: 10, weight: .medium, design: .monospaced)).foregroundStyle(.tertiary).lineSpacing(5)
                }.padding(.bottom, 24)
            }.padding(.horizontal, 20).frame(width: 218).background(Color.white.opacity(0.025))
            Rectangle().fill(Color.white.opacity(0.06)).frame(width: 1)
            ScrollView {
                VStack(alignment: .leading, spacing: 22) {
                    HStack(alignment: .top) {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("一个光标，两个世界").font(.system(size: 29, weight: .bold))
                            Text("跨过屏幕边缘，接着做你的事。").foregroundStyle(.secondary)
                        }
                        Spacer()
                        Label(model.connected ? "已连接" : model.listening ? "等待设备" : model.autoStarting ? "准备共享" : "未连接", systemImage: model.connected ? "circle.fill" : "circle")
                            .font(.system(size: 12, weight: .medium)).foregroundStyle(model.connected ? .mint : .secondary)
                            .padding(.horizontal, 13).padding(.vertical, 9).background(Color.white.opacity(0.04), in: Capsule())
                    }
                    VStack(alignment: .leading, spacing: 16) {
                        HStack {
                            Text("屏幕排列").font(.headline)
                            Spacer()
                            Text("拖动 Windows 屏幕调整位置").font(.system(size: 11)).foregroundStyle(.secondary)
                        }
                        ArrangementView(model: model).frame(height: 252)
                    }.padding(20).background(surface, in: RoundedRectangle(cornerRadius: 18)).overlay(RoundedRectangle(cornerRadius: 18).strokeBorder(.white.opacity(0.06)))
                    HStack(spacing: 14) {
                        infoCard("键盘跟随光标", detail: "当前控制 · \(model.active == "mac" ? "Mac" : "Windows")", icon: "keyboard", color: accent)
                        infoCard("雷雳有线连接", detail: "自动发现 · 自动加密", icon: "bolt.fill", color: .mint)
                    }
                    HStack(spacing:14) {
                        Image(systemName:model.gameProtection ? "lock.fill":"gamecontroller").foregroundStyle(model.gameProtection ? .mint:accent)
                        VStack(alignment:.leading,spacing:6) {
                            Text("Windows 游戏保护").font(.system(size:13,weight:.semibold))
                            Text(model.gameProtectionStatus).font(.system(size:11)).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button(model.gameProtection ? "解除锁定":"锁定 Windows") { model.toggleGameProtection() }
                            .disabled(!model.connected || !model.gameProtectionAvailable || model.active != "windows")
                    }.padding(16).background(surface,in:RoundedRectangle(cornerRadius:14))
                    VStack(alignment: .leading, spacing: 16) {
                        HStack {
                            VStack(alignment: .leading, spacing: 6) {
                                Text("连接设备").font(.headline)
                                Text(model.status).font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                            }
                            Spacer(minLength: 16)
                            Button { model.listening || model.autoStarting ? model.stop() : model.start() } label: {
                                Text(model.listening || model.autoStarting ? "停止共享" : "启用共享").font(.system(size: 13, weight: .semibold)).padding(.horizontal, 20).padding(.vertical, 11)
                            }.buttonStyle(.plain).background(model.listening || model.autoStarting ? Color.white.opacity(0.08) : accent, in: RoundedRectangle(cornerRadius: 10)).foregroundStyle(model.listening || model.autoStarting ? .white : Color(red: 0.06, green: 0.06, blue: 0.13))
                        }
                        Text(model.clipboardStatus).font(.system(size: 11)).foregroundStyle(.mint).fixedSize(horizontal: false, vertical: true)
                        Divider().opacity(0.4)
                        HStack {
                            Text(model.bridge).font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary).textSelection(.enabled)
                            Button { model.refreshBridge() } label: { Image(systemName: "arrow.clockwise") }.buttonStyle(.plain).foregroundStyle(.secondary)
                            Spacer()
                            Button("权限设置") { model.showingPermissions = true }.buttonStyle(.plain).font(.system(size: 11)).foregroundStyle(.secondary)
                        }
                    }.padding(20).background(surface, in: RoundedRectangle(cornerRadius: 18))
                    HStack(spacing: 7) {
                        Image(systemName: "escape"); Text("Ctrl + Alt + Esc 随时停止共享")
                        Spacer(); Text("关闭窗口后继续在状态栏运行")
                    }.font(.system(size: 10)).foregroundStyle(.tertiary)
                }.padding(30).frame(maxWidth: 1000)
            }
        }.background(Color(red: 0.045, green: 0.052, blue: 0.078)).frame(minWidth: 960, minHeight: 710)
            .sheet(isPresented: $model.showingPermissions) { PermissionsView(model: model, permissions: model.permissions) }
    }
    private func infoCard(_ title: String, detail: String, icon: String, color: Color) -> some View {
        HStack(spacing: 13) {
            Image(systemName: icon).font(.system(size: 19)).foregroundStyle(color).frame(width: 42, height: 42).background(color.opacity(0.12), in: RoundedRectangle(cornerRadius: 11))
            VStack(alignment: .leading, spacing: 6) { Text(title).font(.system(size: 13, weight: .semibold)); Text(detail).font(.system(size: 11)).foregroundStyle(.secondary) }
            Spacer(minLength: 0)
        }.padding(16).frame(maxWidth: .infinity).background(surface, in: RoundedRectangle(cornerRadius: 14))
    }
}

struct ArrangementView: View {
    @ObservedObject var model: FlowModel
    @StateObject private var dragState = DragState()
    private var drag: CGSize { get { dragState.value } nonmutating set { dragState.value = newValue } }
    var body: some View {
        GeometryReader { geo in
            let mac = model.topology.mac, win = model.topology.windows
            let minX = min(mac.x,win.x), maxX = max(mac.maxX,win.maxX)
            let minY = min(mac.y,win.y), maxY = max(mac.maxY,win.maxY)
            let width=Double(geo.size.width), height=Double(geo.size.height)
            let scale: Double = min((width-76)/(maxX-minX), (height-50)/(maxY-minY))
            let ox: Double = (width-(maxX-minX)*scale)/2-minX*scale
            let oy: Double = (height-(maxY-minY)*scale)/2-minY*scale
            ZStack {
                Canvas { context, size in
                    for x in stride(from: 10.0, to: size.width, by: 18) { for y in stride(from: 10.0, to: size.height, by: 18) {
                        context.fill(Path(ellipseIn: CGRect(x: x, y: y, width: 1.4, height: 1.4)), with: .color(.white.opacity(0.10)))
                    } }
                }
                monitor("Mac", subtitle: "本机 · 主显示器", icon: "desktopcomputer", selected: model.active == "mac", rect: mac, scale: scale)
                    .position(x: ox+(mac.x+mac.width/2)*scale,y: oy+(mac.y+500)*scale)
                monitor("Windows", subtitle: "远端 · 主显示器", icon: "laptopcomputer", selected: model.active == "windows", rect: win, scale: scale)
                    .position(x: ox+(win.x+win.width/2)*scale+Double(drag.width),y: oy+(win.y+500)*scale+Double(drag.height))
                    .gesture(DragGesture().onChanged { drag = $0.translation }.onEnded { value in
                        let cx = win.x+win.width/2 + Double(value.translation.width)/scale
                        let cy = win.y+500 + Double(value.translation.height)/scale
                        let rx = (cx-mac.width/2)/(mac.width/2+win.width/2)
                        let ry = (cy-500)/1000
                        let side: Side = abs(rx) >= abs(ry) ? (rx < 0 ? .left : .right) : (ry < 0 ? .top : .bottom)
                        let offset = side == .left || side == .right ? cy-500 : cx-win.width/2
                        drag = .zero; model.arrange(side, offset: offset)
                    })
            }.background(Color.black.opacity(0.17), in: RoundedRectangle(cornerRadius: 12))
        }
    }
    private func monitor(_ name: String, subtitle: String, icon: String, selected: Bool, rect: ScreenRect, scale: Double) -> some View {
        VStack(spacing: 7) {
            Image(systemName: icon).font(.system(size: 22, weight: .light))
            Text(name).font(.system(size: 15, weight: .semibold))
            if rect.height*scale > 85 { Text(subtitle).font(.system(size: 9)).foregroundStyle(.secondary) }
        }.frame(width: max(60,rect.width*scale-6), height: max(50,rect.height*scale-6))
            .foregroundStyle(selected ? .white : .white.opacity(0.65))
            .background(LinearGradient(colors: selected ? [accent.opacity(0.45),accent.opacity(0.11)] : [.white.opacity(0.10),.white.opacity(0.025)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 11))
            .overlay(RoundedRectangle(cornerRadius: 11).strokeBorder(selected ? accent : .white.opacity(0.16), lineWidth: selected ? 1.5 : 1))
            .shadow(color: selected ? accent.opacity(0.12) : .clear, radius: 18)
    }
}

final class DragState: ObservableObject { @Published var value: CGSize = .zero }

struct PermissionsView: View {
    @ObservedObject var model: FlowModel
    @ObservedObject var permissions: InputPermissions
    @State private var loginInstalled=LoginSupport.installed
    private var sharing: Bool { model.listening || model.connected }
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Label(sharing ? "键鼠共享正在运行" : "允许键鼠共享", systemImage: sharing ? "checkmark.circle" : "hand.raised")
                .font(.system(size: 23, weight: .bold))
            Text(permissions.status.ready ? (sharing ? "两项权限已开启，正在与远端共享键鼠。" : "两项权限已开启，无需再次授权。") : model.permissionHint)
                .font(.system(size: 13)).foregroundStyle(.secondary)
            permissionRow("辅助功能", detail: "用于控制另一台设备传来的键鼠输入。", allowed: permissions.status.accessibility, permission: .accessibility)
            permissionRow("输入监控", detail: "用于读取本机键鼠，转发到另一台设备。", allowed: permissions.status.monitoring, permission: .monitoring)
            if !permissions.status.ready {
                VStack(alignment: .leading, spacing: 7) {
                    Text("打开设置后，旁边会显示应用小窗口。把图标拖入权限列表，再开启开关。").font(.system(size: 12)).foregroundStyle(.secondary)
                    HStack {
                        Text(Bundle.main.bundleURL.path).font(.system(size: 10, design: .monospaced)).lineLimit(2).textSelection(.enabled)
                        Spacer(); Button("在 Finder 显示") { model.revealApplication() }.buttonStyle(.link).font(.system(size: 11))
                    }
                    Text("返回应用后会自动刷新。若系统提示退出并重新打开，请点击下方按钮。").font(.system(size: 11)).foregroundStyle(.secondary)
                    Text("从旧版升级后，若开关已开启但仍显示未授权，请移除旧记录，再添加上方这份应用。").font(.system(size: 11)).foregroundStyle(.secondary)
                }
            }
            loginSupportSection
            Divider()
            HStack {
                if !permissions.status.ready {
                    Button("重新打开应用") { model.reopen() }
                }
                Spacer()
                if sharing {
                    Button("完成") { model.showingPermissions = false }.buttonStyle(.borderedProminent).tint(accent)
                        .keyboardShortcut(.defaultAction)
                } else {
                    Button("稍后") { model.showingPermissions = false }.keyboardShortcut(.cancelAction)
                    Button("启用共享") { model.start() }.buttonStyle(.borderedProminent).tint(accent)
                        .disabled(!permissions.status.ready).keyboardShortcut(.defaultAction)
                }
            }
        }.padding(28).frame(width: 530).preferredColorScheme(.dark)
            .onAppear { model.watchPermissions();loginInstalled=LoginSupport.installed }
            .onDisappear { model.stopWatchingPermissions() }
            .onReceive(NotificationCenter.default.publisher(for:NSApplication.didBecomeActiveNotification)) { _ in
                loginInstalled=LoginSupport.installed
            }
    }
    private var loginSupportSection: some View {
        VStack(alignment:.leading,spacing:12) {
            HStack(spacing:12) {
                Image(systemName:"lock.shield").font(.system(size:23)).foregroundStyle(accent)
                VStack(alignment:.leading,spacing:6) {
                    HStack {
                        Text("登录前共享").fontWeight(.semibold)
                        Text(loginInstalled ? "已安装":"未安装").font(.system(size:11))
                            .foregroundStyle(loginInstalled ? .mint:.secondary)
                    }
                    Text("在 Mac 登录页与锁屏页接收远端键鼠。").font(.system(size:12)).foregroundStyle(.secondary)
                }
            }
            Text(loginInstalled ? "更新应用后，可在这里同步更新登录组件。安装或移除时需要管理员授权。":"先与 Windows 成功连接一次，再安装登录组件。安装时需要管理员授权。")
                .font(.system(size:11)).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            HStack(spacing:14) {
                if loginInstalled {
                    Button("运行状态") {
                        let alert=NSAlert();alert.messageText="登录前共享状态"
                        alert.informativeText=LoginStatusFile.description;alert.runModal()
                    }.buttonStyle(.link)
                    Button("移除组件…") { loginAction { try LoginSupport.remove() } }.buttonStyle(.link)
                }
                Spacer()
                Button(loginInstalled ? "更新组件…":"安装组件…") {
                    loginAction { try LoginSupport.prepare(side:model.side,offset:model.offset) }
                }.buttonStyle(.bordered).tint(accent)
            }
        }.padding(16).background(surface,in:RoundedRectangle(cornerRadius:12))
    }
    private func loginAction(_ action:() throws -> Void) {
        do { try action() }
        catch { let alert=NSAlert();alert.messageText="登录前共享";alert.informativeText=error.localizedDescription;alert.runModal() }
    }
    private func permissionRow(_ title: String, detail: String, allowed: Bool, permission: InputPermission) -> some View {
        HStack(spacing: 13) {
            Image(systemName: allowed ? "checkmark.circle.fill" : "circle.dashed").font(.system(size: 23)).foregroundStyle(allowed ? .mint : accent)
            VStack(alignment: .leading, spacing: 6) {
                HStack { Text(title).fontWeight(.semibold); Text(allowed ? "已开启" : "未开启").font(.system(size: 11)).foregroundStyle(allowed ? .mint : .secondary) }
                Text(detail).font(.system(size: 12)).foregroundStyle(.secondary)
            }
            Spacer()
            Button(allowed ? "查看设置" : "去授权") { model.openPermission(permission) }
        }.padding(16).background(surface, in: RoundedRectangle(cornerRadius: 12))
    }
}
