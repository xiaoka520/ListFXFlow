import Foundation
import OSLog
import Network
import CryptoKit
import FlowCore
import Darwin

/// Connection state and cryptography live on a dedicated queue. Event taps only
/// enqueue sends; network decoding and callbacks cannot occupy their run loop.
final class FlowServer {
    private let connectionLogger=Logger(subsystem:"com.listfxflow.mac",category:"sharing-connection")
    var onMessage: ((Message) -> Void)?
    var onStatus: ((Bool, String) -> Void)?
    var onAuthenticatedAddress:((String)->Void)?
    var onListeningReady:(()->Void)?
    private let networkQueue=DispatchQueue(label:"ListFXFlow.input.network",qos:.userInteractive)
    private let delivery=MessageDeliveryGate()
    private var listeners: [NWListener] = []
    private var connection: NWConnection?
    private var decoder = FrameDecoder()
    private var secure: SecureChannel?
    private var clientRandom = Data(), serverRandom = Data(), secret = Data(), peer = Data()
    private var identity: P256.KeyAgreement.PrivateKey?
    // Keychain access may be unavailable while locked. Preserve the authenticated
    // peer in memory instead of treating a locked keychain as a new pairing.
    private var trustedPeer:Data?
    private var allowPairing:Bool
    private var stage = 0
    private var lastReceived = ProcessInfo.processInfo.systemUptime
    private var deadline = ProcessInfo.processInfo.systemUptime
    private var timer: DispatchSourceTimer?
    private var queuedBytes = 0
    private var discovery: DiscoveryResponder?
    private var connected = false
    var isListening: Bool { networkQueue.sync { !listeners.isEmpty } }
    private let identityProvider: () throws -> P256.KeyAgreement.PrivateKey
    private let loadPeer: () -> Data?
    private let savePeer: (Data) throws -> Void
    init(identityProvider: @escaping () throws -> P256.KeyAgreement.PrivateKey = IdentityStore.key,
         loadPeer: @escaping () -> Data? = { IdentityStore.read("peer") },
         savePeer: @escaping (Data) throws -> Void = { try IdentityStore.write($0, account: "peer") },requirePinnedPeer:Bool=false) {
        self.identityProvider = identityProvider; self.loadPeer = loadPeer; self.savePeer = savePeer
        allowPairing = !requirePinnedPeer
    }

    func start(bridges: [BridgeAddress], port: UInt16 = 49234, discoveryPort: UInt16 = 49235) throws {
        try networkQueue.sync { try startCore(bridges:bridges,port:port,discoveryPort:discoveryPort) }
    }
    private func publishStatus(_ ready:Bool,_ value:String) {
        let ticket=delivery.current
        DispatchQueue.main.async { [weak self] in guard let self,self.delivery.valid(ticket) else{return};self.onStatus?(ready,value) }
    }
    private func publishMessage(_ value:Message) {
        let ticket=delivery.current
        DispatchQueue.main.async { [weak self] in guard let self,self.delivery.valid(ticket) else{return};self.onMessage?(value) }
    }
    private func startCore(bridges: [BridgeAddress], port: UInt16, discoveryPort: UInt16) throws {
        stopCore()
        if identity == nil { identity = try identityProvider() }
        if trustedPeer == nil { trustedPeer = loadPeer() }
        if !allowPairing && trustedPeer == nil { throw FlowMacError.changedPeer }
        guard let bridge = bridges.first else { throw FlowMacError.bridge }
        for address in bridges {
        let tcp = NWProtocolTCP.Options(); tcp.noDelay = true
        let parameters = NWParameters(tls: nil, tcp: tcp)
        parameters.requiredLocalEndpoint = .hostPort(host: .init(address.address), port: NWEndpoint.Port(rawValue: port)!)
        if bridge.name != "lo0" { parameters.prohibitedInterfaceTypes = [.wifi, .cellular, .loopback] }
        parameters.allowLocalEndpointReuse = true
        let listener = try NWListener(using: parameters)
        listener.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
        listener.stateUpdateHandler = { [weak self, weak listener] state in
            guard let self,let listener,self.listeners.contains(where: { $0 === listener }) else { return }
            switch state {
            case .ready:
                let ticket=self.delivery.current
                DispatchQueue.main.async { [weak self] in
                    guard let self,self.delivery.valid(ticket) else{return};self.onListeningReady?()
                }
                if !self.connected { self.publishStatus(false, "等待 Windows · 雷雳直连已就绪") }
            case .failed(let error): self.stopCore(); self.publishStatus(false, "监听失败：\(error.localizedDescription)")
            default: break
            }
        }
        listeners.append(listener)
        listener.start(queue: networkQueue)
        }
        discovery = try DiscoveryResponder(bridges: bridges, port: discoveryPort, servicePort: port)
        let heartbeat=DispatchSource.makeTimerSource(queue:networkQueue)
        heartbeat.schedule(deadline:.now()+1,repeating:1)
        heartbeat.setEventHandler { [weak self] in self?.tick() };heartbeat.resume();timer=heartbeat
    }
    private func accept(_ incoming: NWConnection) {
        guard connection == nil else { incoming.cancel(); return }
        delivery.advance()
        connection = incoming; stage = 0; decoder = FrameDecoder(); queuedBytes = 0
        deadline = ProcessInfo.processInfo.systemUptime + 5
        incoming.stateUpdateHandler = { [weak self, weak incoming] state in
            guard let self, self.connection === incoming else { return }
            switch state {
            case .ready: self.receive()
            case .failed, .cancelled: self.disconnectCore("连接已断开 · 已恢复本机控制")
            default: break
            }
        }
        incoming.start(queue: networkQueue)
    }
    private func receive() {
        guard let current = connection else { return }
        current.receive(minimumIncompleteLength: 1, maximumLength: 16384) { [weak self, weak current] bytes, _, complete, error in
            guard let self, self.connection === current else { return }
            do {
                if let bytes { for frame in try self.decoder.append(bytes) { try self.handle(frame) } }
                if complete || error != nil { self.disconnectCore("连接已断开 · 已恢复本机控制") }
                else { self.receive() }
            } catch { self.disconnectCore(error.localizedDescription) }
        }
    }
    private func handle(_ frame: Data) throws {
        if let secure {
            let message = try secure.open(frame); lastReceived = ProcessInfo.processInfo.systemUptime; publishMessage(message); return
        }
        let message = try JSONDecoder().decode(Message.self, from: frame)
        guard let identity else { throw FlowMacError.identity }
        if stage == 0 {
            guard message.type == "hello", message.version == 1,
                  let random = Data(hex: message.nonce ?? ""), random.count == 32,
                  let publicKey = Data(hex: message.publicKey ?? ""), publicKey.count == 65 else { throw FlowMacError.protocolError }
            if !allowPairing && trustedPeer == nil { throw FlowMacError.changedPeer }
            if let trusted = trustedPeer, trusted != publicKey { throw FlowMacError.changedPeer }
            peer = publicKey; clientRandom = random; serverRandom = .random(32)
            secret = try Authentication.sharedSecret(privateKey: identity, peer: publicKey)
            var challenge = Message("challenge"); challenge.nonce = serverRandom.hex
            challenge.publicKey = identity.publicKey.x963Representation.hex; challenge.version = 1
            challenge.auth = Authentication.proof(secret: secret, role: "server", client: clientRandom, server: serverRandom).hex
            sendPlain(challenge); stage = 1
        } else {
            guard message.type == "finish", let proof = Data(hex: message.auth ?? ""),
                  Authentication.verify(proof, secret: secret, role: "client", client: clientRandom, server: serverRandom) else { throw FlowMacError.protocolError }
            if trustedPeer == nil { try savePeer(peer);trustedPeer=peer }
            secure = SecureChannel(secret: secret, client: clientRandom, server: serverRandom, isServer: true)
            secret = Data(); connected = true; lastReceived = ProcessInfo.processInfo.systemUptime
            if case .hostPort(let host,_) = connection?.currentPath?.localEndpoint {
                let address=host.debugDescription,ticket=delivery.current
                DispatchQueue.main.async { [weak self] in
                    guard let self,self.delivery.valid(ticket) else{return}
                    self.onAuthenticatedAddress?(address)
                }
            }
            sendCore(Message("ping")); publishStatus(true, "已连接远端 · 加密雷雳直连")
        }
    }
    func send(_ message: Message) {
        let gate=delivery
        guard let ticket=gate.reserve() else { disconnect("输入发送积压 · 已恢复本机控制");return }
        networkQueue.async { [weak self] in
            defer { gate.release() }
            guard gate.valid(ticket) else{return}
            self?.sendCore(message)
        }
    }
    private func sendCore(_ message: Message) {
        guard let secure, connected else { return }
        do { transmit(try secure.seal(message)) } catch { disconnectCore("消息校验失败，已停止共享") }
    }
    private func sendPlain(_ message: Message) {
        do { transmit(try JSONEncoder().encode(message)) } catch { disconnectCore("握手失败") }
    }
    private func transmit(_ payload: Data) {
        guard let current = connection else { return }
        let bytes = FrameDecoder.frame(payload)
        guard queuedBytes + bytes.count < 262144 else { disconnectCore("连接拥塞，已恢复本机控制"); return }
        queuedBytes += bytes.count
        current.send(content: bytes, completion: .contentProcessed { [weak self, weak current] error in
            guard let self, self.connection === current else { return }
            self.queuedBytes -= bytes.count
            if error != nil { self.disconnectCore("发送失败，已恢复本机控制") }
        })
    }
    private func tick() {
        if connection != nil, !connected, ProcessInfo.processInfo.systemUptime > deadline { disconnectCore("设备握手超时") }
        if connected {
            if ProcessInfo.processInfo.systemUptime - lastReceived > 3 { disconnectCore("雷雳连接超时 · 已恢复本机控制") }
            else { sendCore(Message("ping")) }
        }
    }
    func disconnect(_ reason:String = "共享已停止") { networkQueue.async { [weak self] in self?.disconnectCore(reason) } }
    func requireKnownPeer() { networkQueue.async { [weak self] in
        guard let self else{return};self.allowPairing=false
        if self.trustedPeer == nil { self.disconnectCore("登录窗口仅允许已配对设备") }
    } }
    private func disconnectCore(_ reason: String = "共享已停止") {
        if connection != nil || !listeners.isEmpty {
            connectionLogger.notice("disconnect reason=\(reason,privacy:.public) hadConnection=\(self.connection != nil,privacy:.public) listenerCount=\(self.listeners.count,privacy:.public)")
        }
        delivery.advance()
        let old = connection; connection = nil; old?.cancel()
        connected = false; secure = nil; secret = Data(); queuedBytes = 0
        publishStatus(false, reason)
    }
    func stop() { networkQueue.sync { stopCore() } }
    private func stopCore() {
        disconnectCore(); for listener in listeners { listener.cancel() }; listeners.removeAll(); timer?.cancel(); timer = nil
        discovery = nil
    }
}

final class DiscoveryResponder {
    private let fd: Int32
    private var source: DispatchSourceRead?
    private let bridges: [BridgeAddress]
    private let servicePort: UInt16
    init(bridges: [BridgeAddress], port: UInt16 = 49235, servicePort: UInt16 = 49234) throws {
        self.bridges = bridges; self.servicePort = servicePort
        guard let bridge = bridges.first else { throw FlowMacError.bridge }
        let socketFD = socket(AF_INET, SOCK_DGRAM, 0)
        guard socketFD >= 0 else { throw FlowMacError.bridge }
        var index = if_nametoindex(bridge.name), yes: Int32 = 1
        guard setsockopt(socketFD, IPPROTO_IP, IP_BOUND_IF, &index, socklen_t(MemoryLayout.size(ofValue: index))) == 0 else { close(socketFD); throw FlowMacError.bridge }
        setsockopt(socketFD, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout.size(ofValue: yes)))
        setsockopt(socketFD, SOL_SOCKET, SO_BROADCAST, &yes, socklen_t(MemoryLayout.size(ofValue: yes)))
        var address = sockaddr_in(); address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        address.sin_family = sa_family_t(AF_INET); address.sin_port = port.bigEndian
        let status = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(socketFD, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        guard status == 0 else { close(socketFD); throw FlowMacError.bridge }
        fd = socketFD
        _ = fcntl(socketFD, F_SETFL, O_NONBLOCK)
        let source = DispatchSource.makeReadSource(fileDescriptor: fd, queue: .main)
        source.setEventHandler { [weak self] in self?.respond() }; source.resume(); self.source = source
    }
    private func respond() {
        var bytes = [UInt8](repeating: 0, count: 256); var sender = sockaddr_in(); var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        let n = withUnsafeMutablePointer(to: &sender) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { recvfrom(fd, &bytes, bytes.count, 0, $0, &length) } }
        guard n > 0, String(bytes: bytes.prefix(n), encoding: .utf8) == "ListFXFlow.discover.v1" else { return }
        for bridge in bridges {
            var ip = in_addr(), mask = in_addr()
            guard inet_pton(AF_INET, bridge.address, &ip) == 1, inet_pton(AF_INET, bridge.mask, &mask) == 1,
                  ip.s_addr & mask.s_addr == sender.sin_addr.s_addr & mask.s_addr else { continue }
            // Bind the reply to the advertised address, including when bridge0 has several IPv4 aliases.
            let reply = socket(AF_INET, SOCK_DGRAM, 0); guard reply >= 0 else { continue }; defer { close(reply) }
            var local = sockaddr_in(); local.sin_len = UInt8(MemoryLayout<sockaddr_in>.size); local.sin_family = sa_family_t(AF_INET); local.sin_addr = ip
            let bound = withUnsafePointer(to: &local) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(reply, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
            guard bound == 0, let response = try? JSONSerialization.data(withJSONObject: ["service": "ListFXFlow/1", "address": bridge.address, "port": servicePort]) else { continue }
            _ = response.withUnsafeBytes { data in withUnsafePointer(to: &sender) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { sendto(reply, data.baseAddress, data.count, 0, $0, length) } } }
            return // Advertise the first compatible address, preserving the saved preference.
        }
    }
    deinit { source?.cancel(); close(fd) }
}
