import Foundation
import CryptoKit
import FlowCore

/// Explicit development mode: loopback only, ephemeral identities, no event tap or input injection.
enum TransportSelfTest {
    static func run() -> Never {
        let identity = P256.KeyAgreement.PrivateKey()
        let server = FlowServer(identityProvider: { identity }, loadPeer: { nil }, savePeer: { _ in })
        let clipboard = ClipboardBridge(testMode: true)
        clipboard.onStatus = { print("CLIPBOARD-SERVER \($0)"); fflush(stdout) }
        let bridges = [BridgeAddress(name: "lo0", address: "127.0.0.1", broadcast: "127.255.255.255", mask: "255.0.0.0")]
        server.onStatus = { connected, status in if !connected { clipboard.stop() }; print("TRANSPORT \(connected) \(status)"); fflush(stdout) }
        server.onMessage = { message in
            if message.type == "display", message.features?.contains("clipboard-v1") == true {
                do { try clipboard.start(bridges: bridges,port: 49336) { token in var ready=Message("clipboardReady"); ready.nonce=token.hex; server.send(ready) } }
                catch { print("CLIPBOARD ERROR \(error)"); fflush(stdout) }
            }
            server.send(message)
        }
        do { try server.start(bridges: [BridgeAddress(name: "lo0", address: "127.0.0.1", broadcast: "127.255.255.255", mask: "255.0.0.0")],port: 49334,discoveryPort: 49335) }
        catch { print(error); exit(1) }
        withExtendedLifetime(server) { RunLoop.main.run() }
        exit(1)
    }
}
