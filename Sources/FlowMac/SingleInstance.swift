import Foundation
import Darwin

/// A per-user advisory lock keeps one background instance per login session. The
/// lock is released by the kernel when the process exits, so a crash cannot leave a
/// stale lock behind. A second launch asks the existing instance to show its window.
final class SingleInstance {
    static let showNotification = Notification.Name("com.listfxflow.mac.show")
    private var descriptor: Int32 = -1
    func acquire() -> Bool {
        let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ListFXFlow", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let path = folder.appendingPathComponent("instance.lock").path
        descriptor = open(path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard descriptor >= 0 else { return true }
        if flock(descriptor, LOCK_EX | LOCK_NB) == 0 { return true }
        close(descriptor); descriptor = -1
        DistributedNotificationCenter.default().post(name: Self.showNotification, object: nil)
        // Give notifyd a moment to deliver the request before this process goes away.
        usleep(100_000)
        return false
    }
    deinit {
        guard descriptor >= 0 else { return }
        flock(descriptor, LOCK_UN)
        close(descriptor)
    }
}
