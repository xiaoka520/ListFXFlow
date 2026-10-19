import ApplicationServices
import CoreGraphics
import Combine

public enum InputPermission { case accessibility, monitoring }
public struct InputPermissionStatus: Equatable {
    public var accessibility: Bool
    public var monitoring: Bool
    public var ready: Bool { accessibility && monitoring }
}

/// Requests are explicit and independent. A prompt returning false means pending, not failure.
public final class InputPermissions: ObservableObject {
    @Published public private(set) var status: InputPermissionStatus
    private let readAccessibility: () -> Bool
    private let readMonitoring: () -> Bool
    private let requestAccessibility: () -> Void
    private let requestMonitoring: () -> Void
    public init(readAccessibility: @escaping () -> Bool = { AXIsProcessTrusted() },
                readMonitoring: @escaping () -> Bool = { CGPreflightListenEventAccess() },
                requestAccessibility: @escaping () -> Void = {
                    _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary)
                },
                requestMonitoring: @escaping () -> Void = { _ = CGRequestListenEventAccess() }) {
        self.readAccessibility = readAccessibility; self.readMonitoring = readMonitoring
        self.requestAccessibility = requestAccessibility; self.requestMonitoring = requestMonitoring
        status = InputPermissionStatus(accessibility: readAccessibility(), monitoring: readMonitoring())
    }
    public func refresh() { status = InputPermissionStatus(accessibility: readAccessibility(), monitoring: readMonitoring()) }
    public func request(_ permission: InputPermission) {
        switch permission {
        case .accessibility: if !status.accessibility { requestAccessibility() }
        case .monitoring: if !status.monitoring { requestMonitoring() }
        }
        refresh()
    }
}
