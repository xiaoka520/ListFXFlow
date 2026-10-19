import AppKit
import SwiftUI
import FlowCore

/// A nonactivating companion to System Settings. The drag exports the running app's file URL.
final class PermissionDragPanel: NSWindowController, NSWindowDelegate {
    private var positionTimer: Timer?

    init() {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 300, height: 310),
                            styleMask: [.titled, .closable, .utilityWindow, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        panel.title = "拖入系统设置"
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.worksWhenModal = true
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.appearance = NSAppearance(named: .darkAqua)
        panel.isMovableByWindowBackground = true
        panel.animationBehavior = .utilityWindow
        super.init(window: panel)
        panel.delegate = self
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func show(for permission: InputPermission) {
        guard let panel = window else { return }
        let name = permission == .accessibility ? "辅助功能" : "输入监控"
        panel.contentView = NSHostingView(rootView: PermissionDragView(permissionName: name, close: { [weak self] in self?.close() }))
        panel.setContentSize(NSSize(width: 300, height: 310))
        if !positionBesideSettings() { positionAtScreenEdge() }
        // Keep System Settings focused so the user can drag directly into its list.
        panel.orderFrontRegardless()
        positionTimer?.invalidate()
        var attempts = 0
        positionTimer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] timer in
            attempts += 1
            if self?.positionBesideSettings() == true || attempts >= 16 { timer.invalidate(); self?.positionTimer = nil }
        }
    }

    func windowWillClose(_ notification: Notification) { positionTimer?.invalidate(); positionTimer = nil }

    private func positionAtScreenEdge() {
        guard let panel = window, let screen = NSApp.mainWindow?.screen ?? NSScreen.main else { return }
        let area = screen.visibleFrame
        panel.setFrameOrigin(NSPoint(x: area.maxX - panel.frame.width - 18, y: area.maxY - panel.frame.height - 24))
    }

    private func positionBesideSettings() -> Bool {
        guard let panel = window,
              let pid = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.systempreferences").first?.processIdentifier,
              let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]],
              let mainTop = NSScreen.screens.first?.frame.maxY else { return false }
        // Bounds and owner PID are public window metadata; no window capture or Accessibility grant is needed.
        let candidates = windows.compactMap { item -> CGRect? in
            guard (item[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == pid,
                  (item[kCGWindowLayer as String] as? NSNumber)?.intValue == 0,
                  let bounds = item[kCGWindowBounds as String] as? [String: Any],
                  let rect = CGRect(dictionaryRepresentation: bounds as CFDictionary), rect.width >= 350, rect.height >= 300 else { return nil }
            return CGRect(x: rect.minX, y: mainTop - rect.maxY, width: rect.width, height: rect.height)
        }
        guard let settings = candidates.max(by: { $0.width * $0.height < $1.width * $1.height }),
              let screen = NSScreen.screens.max(by: {
                  let a = $0.frame.intersection(settings), b = $1.frame.intersection(settings)
                  return (a.isNull ? 0 : a.width * a.height) < (b.isNull ? 0 : b.width * b.height)
              }) else { return false }
        let area = screen.visibleFrame, size = panel.frame.size, gap: CGFloat = 12
        let right = settings.maxX + gap, left = settings.minX - size.width - gap
        let x: CGFloat
        if right + size.width <= area.maxX { x = right }
        else if left >= area.minX { x = left }
        else { x = area.maxX - settings.maxX >= settings.minX - area.minX ? area.maxX - size.width : area.minX }
        let y = min(area.maxY - size.height, max(area.minY, settings.maxY - size.height))
        panel.setFrameOrigin(NSPoint(x: min(max(area.minX, x), area.maxX - size.width), y: y))
        return true
    }

    deinit { positionTimer?.invalidate() }
}

private struct PermissionDragView: View {
    let permissionName: String
    let close: () -> Void
    private let appURL = Bundle.main.bundleURL
    var body: some View {
        VStack(spacing: 12) {
            Text("把应用拖到设置里").font(.system(size: 17, weight: .semibold))
            Text("拖入「\(permissionName)」列表").font(.system(size: 12)).foregroundStyle(.secondary)
            VStack(spacing: 7) {
                DraggableApplicationIcon(appURL: appURL).frame(width: 76, height: 76)
                Text(appURL.lastPathComponent).font(.system(size: 13, weight: .medium))
                Text("按住图标拖动").font(.system(size: 11)).foregroundStyle(.secondary)
            }.frame(maxWidth: .infinity).padding(.vertical, 12)
                .background(Color.white.opacity(0.045), in: RoundedRectangle(cornerRadius: 12))
                .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.white.opacity(0.10)))
            Text("添加后，开启应用右侧的权限开关。").font(.system(size: 11)).foregroundStyle(.secondary)
            HStack {
                Button("在 Finder 显示") { NSWorkspace.shared.activateFileViewerSelecting([appURL]) }.buttonStyle(.link)
                Spacer()
                Button("完成", action: close)
            }.font(.system(size: 11))
        }.padding(20).frame(width: 300, height: 310).preferredColorScheme(.dark)
    }
}

private struct DraggableApplicationIcon: NSViewRepresentable {
    let appURL: URL
    func makeNSView(context: Context) -> ApplicationDragIcon { ApplicationDragIcon(appURL: appURL) }
    func updateNSView(_ view: ApplicationDragIcon, context: Context) {}
}

private final class ApplicationDragIcon: NSView, NSDraggingSource {
    private let appURL: URL
    private let icon: NSImage
    private var startPoint: NSPoint?
    private var dragging = false
    init(appURL: URL) {
        self.appURL = appURL
        icon = NSWorkspace.shared.icon(forFile: appURL.path)
        super.init(frame: NSRect(x: 0, y: 0, width: 76, height: 76))
        setAccessibilityElement(true)
        setAccessibilityRole(.image)
        setAccessibilityLabel(appURL.lastPathComponent)
        setAccessibilityHelp("按住应用图标，拖入系统设置的权限列表。")
        toolTip = "拖动 \(appURL.lastPathComponent) 到系统设置"
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .openHand) }
    override func draw(_ dirtyRect: NSRect) { icon.draw(in: bounds.insetBy(dx: 4, dy: 4)) }
    override func mouseDown(with event: NSEvent) { startPoint = convert(event.locationInWindow, from: nil) }
    override func mouseUp(with event: NSEvent) { startPoint = nil }
    override func mouseDragged(with event: NSEvent) {
        guard !dragging, let startPoint else { return }
        let point = convert(event.locationInWindow, from: nil)
        guard hypot(point.x - startPoint.x, point.y - startPoint.y) >= 3 else { return }
        dragging = true
        // NSURL conforms to NSPasteboardWriting and exports public.file-url, not a screenshot or icon image.
        let item = NSDraggingItem(pasteboardWriter: appURL as NSURL)
        item.setDraggingFrame(NSRect(x: point.x - 34, y: point.y - 34, width: 68, height: 68), contents: icon)
        let session = beginDraggingSession(with: [item], event: event, source: self)
        session.animatesToStartingPositionsOnCancelOrFail = true
        session.draggingFormation = .none
    }
    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation { .copy }
    func ignoreModifierKeys(for session: NSDraggingSession) -> Bool { true }
    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) { dragging = false; startPoint = nil }
}
