import AppKit
import Combine

final class StatusItemController: NSObject {
    private let model: FlowModel
    private let show: () -> Void
    private let item=NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let stateItem=NSMenuItem(title: "ListFXFlow",action: nil,keyEquivalent: "")
    private let toggleItem=NSMenuItem(title: "启用共享",action: #selector(toggle),keyEquivalent: "")
    private let gameItem=NSMenuItem(title:"锁定 Windows",action:#selector(toggleGame),keyEquivalent:"")
    private var observers=Set<AnyCancellable>()
    init(model: FlowModel,show: @escaping () -> Void) {
        self.model=model; self.show=show; super.init()
        let image=NSImage(systemSymbolName: "cursorarrow.motionlines",accessibilityDescription: "ListFXFlow")
        image?.isTemplate=true; item.button?.image=image
        let menu=NSMenu();menu.autoenablesItems=false; stateItem.isEnabled=false; menu.addItem(stateItem); menu.addItem(.separator())
        let open=NSMenuItem(title: "打开主界面…",action: #selector(openMain),keyEquivalent: "o"); open.target=self; menu.addItem(open)
        let permissions=NSMenuItem(title: "权限设置…",action: #selector(openPermissions),keyEquivalent: ""); permissions.target=self; menu.addItem(permissions)
        toggleItem.target=self; menu.addItem(toggleItem); menu.addItem(.separator())
        gameItem.target=self;menu.addItem(gameItem);menu.addItem(.separator())
        let quit=NSMenuItem(title: "退出 ListFXFlow",action: #selector(quit),keyEquivalent: "q"); quit.target=self; menu.addItem(quit)
        item.menu=menu
        Publishers.CombineLatest3(model.$status,model.$listening,model.$autoStarting).receive(on: RunLoop.main).sink { [weak self] status,listening,starting in
            self?.stateItem.title=status; self?.item.button?.toolTip="ListFXFlow · "+status
            self?.toggleItem.title=listening || starting ? "停止共享" : "启用共享"
        }.store(in: &observers)
        Publishers.CombineLatest4(model.$gameProtection,model.$gameProtectionAvailable,model.$active,model.$connected)
            .receive(on:RunLoop.main).sink { [weak self] locked,available,active,connected in
                self?.gameItem.title=locked ? "解除 Windows 游戏锁定":"锁定 Windows · Ctrl + Option + L"
                self?.gameItem.isEnabled=available && connected && active=="windows"
            }.store(in:&observers)
    }
    @objc private func openMain() { show() }
    @objc private func openPermissions() { model.showingPermissions = true; show() }
    @objc private func toggle() {
        if model.listening || model.autoStarting { model.stop() }
        else { model.start(); if model.showingPermissions { show() } }
    }
    @objc private func toggleGame() { model.toggleGameProtection() }
    @objc private func quit() { NSApp.terminate(nil) }
    deinit { NSStatusBar.system.removeStatusItem(item) }
}
