import AppKit
import Network
import CryptoKit
import FlowCore

private final class ClipboardSocket: @unchecked Sendable {
    let connection: NWConnection
    var secure: SecureChannel?
    init(_ connection: NWConnection) { self.connection=connection }
    func read(_ count: Int) async throws -> Data {
        var result=Data()
        while result.count < count {
            let needed=count-result.count
            let part: Data = try await withCheckedThrowingContinuation { continuation in
                connection.receive(minimumIncompleteLength: 1,maximumLength: needed) { data,_,done,error in
                    if let error { continuation.resume(throwing: error) }
                    else if let data, !data.isEmpty { continuation.resume(returning: data) }
                    else { continuation.resume(throwing: ClipboardError.incomplete) }
                }
            }
            result.append(part)
        }
        return result
    }
    func frame() async throws -> Data {
        let header=try await read(4), length=header.reduce(0) { ($0 << 8) | Int($1) }
        guard (1...1_048_576).contains(length) else { throw ClipboardError.invalid }
        return try await read(length)
    }
    func sendData(_ data: Data) async throws {
        guard data.count <= 1_048_576 else { throw ClipboardError.invalid }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void,Error>) in
            connection.send(content: FrameDecoder.frame(data),completion: .contentProcessed { error in
                if let error { continuation.resume(throwing: error) } else { continuation.resume() }
            })
        }
    }
}

/// All file work and crypto run outside the event-tap/main queue, on a separate TCP stream.
private actor ClipboardSession {
    private let socket: ClipboardSocket
    private let secret: Data
    private let echo: Bool
    private let inbox: ClipboardInbox
    private var outbound: Task<Void,Never>?
    private var outboundID: String?
    private var outboundFiles=false
    private var generation=0
    private var inboundGeneration=0
    private var closed=false
    private let apply: @Sendable (ClipboardResult,Int) async -> Bool
    private let status: @Sendable (String) async -> Void
    private let ready: @Sendable () async -> Void
    private let ended: @Sendable () async -> Void
    init(connection: NWConnection, secret: Data, echo: Bool = false, cache: URL? = nil, apply: @escaping @Sendable (ClipboardResult,Int) async -> Bool, status: @escaping @Sendable (String) async -> Void, ready: @escaping @Sendable () async -> Void, ended: @escaping @Sendable () async -> Void) {
        socket=ClipboardSocket(connection); self.secret=secret; self.apply=apply; self.status=status
        self.ready=ready; self.ended=ended; self.echo=echo
        let base=FileManager.default.urls(for: .applicationSupportDirectory,in: .userDomainMask)[0]
        inbox=ClipboardInbox(cache: cache ?? base.appendingPathComponent("ListFXFlow/ClipboardReceived",isDirectory: true))
    }
    func run() async {
        do {
            let hello=try JSONDecoder().decode(Message.self,from: await socket.frame())
            guard hello.type == "hello", let cn=Data(hex: hello.nonce ?? ""), cn.count == 32,
                  let proof=Data(hex: hello.auth ?? ""), Authentication.verify(proof,secret: secret,role: "clipboard-client",client: cn,server: Data()) else { throw ClipboardError.invalid }
            let sn=Data.random(32)
            var challenge=Message("challenge"); challenge.nonce=sn.hex; challenge.auth=Authentication.proof(secret: secret,role: "clipboard-server",client: cn,server: sn).hex
            try await socket.sendData(JSONEncoder().encode(challenge))
            let finish=try JSONDecoder().decode(Message.self,from: await socket.frame())
            guard finish.type == "finish", let proof=Data(hex: finish.auth ?? ""), Authentication.verify(proof,secret: secret,role: "clipboard-finish",client: cn,server: sn) else { throw ClipboardError.invalid }
            let key=Data(SHA256.hash(data: Data("ListFXFlow/Clipboard/1".utf8)+secret))
            socket.secure=SecureChannel(secret: key,client: cn,server: sn,isServer: true)
            await ready()
            await status("剪贴板已连接")
            while !closed {
                let packet=try JSONDecoder().decode(ClipboardPacket.self,from: socket.secure!.openData(await socket.frame()))
                try packet.validate()
                if packet.kind == "done" { if packet.id == outboundID { await status(outboundFiles ? "文件已同步 · 可在远端粘贴" : "文字剪贴板已就绪") }; continue }
                if packet.kind == "cancel" { if packet.id == outboundID { outbound?.cancel() }; _ = try inbox.receive(packet); continue }
                if packet.kind == "begin" || packet.kind == "text" { inboundGeneration=generation }
                do {
                    let result=try inbox.receive(packet)
                    if let result {
                        let applied = echo ? true : await apply(result,inboundGeneration)
                        try await send(ClipboardPacket(applied ? "done" : "cancel",id: packet.id))
                        await status(applied ? "剪贴板已就绪" : "本机剪贴板已变化，未覆盖新内容")
                        if echo {
                            switch result { case .text(let text): await localChanged(text: text,urls: nil,generation: 0); case .files(let urls): await localChanged(text: nil,urls: urls,generation: 0) }
                        }
                    }
                    else if packet.kind == "begin" { await status("正在接收文件…") }
                    else if packet.kind == "data", inbox.id == packet.id, inbox.received % (196608*8) < 196608 { await status("正在接收文件 · \(inbox.received/1024/1024) / \(inbox.total/1024/1024) MB") }
                } catch {
                    inbox.abort(); var cancel=ClipboardPacket("cancel",id: packet.id); cancel.text=nil
                    try await send(cancel); await status(error.localizedDescription)
                }
            }
        } catch { if !closed { await status("剪贴板连接已停止：\(error.localizedDescription)") } }
        close(); await ended()
    }
    func close() { closed=true; outbound?.cancel(); outbound=nil; inbox.abort(); socket.connection.cancel() }
    private func send(_ packet: ClipboardPacket) async throws {
        try Task.checkCancellation(); guard !closed, let secure=socket.secure else { throw ClipboardError.incomplete }
        try await socket.sendData(secure.sealData(JSONEncoder().encode(packet)))
    }
    func localChanged(text: String?, urls: [URL]?, generation: Int) async {
        self.generation=generation
        if let id=inbox.id { inbox.abort(); try? await send(ClipboardPacket("cancel",id: id)) }
        outbound?.cancel()
        let id=UUID().uuidString.lowercased(); outboundID=id; outboundFiles=urls != nil
        outbound=Task { await self.publish(id: id,text: text,urls: urls) }
    }
    private func publish(id: String,text: String?,urls: [URL]?) async {
        do {
            guard socket.secure != nil else { return }
            if let urls {
                let manifest=try ClipboardManifest(urls: urls)
                try Task.checkCancellation()
                var begin=ClipboardPacket("begin",id: id); begin.roots=manifest.roots; begin.count=manifest.entries.count; begin.total=manifest.total
                try await send(begin); await status("正在发送文件…")
                for entry in manifest.entries {
                    var packet=ClipboardPacket("entry",id: id); packet.path=entry.path; packet.size=entry.size; packet.directory=entry.directory
                    try await send(packet)
                    if !entry.directory {
                        let info=try entry.url.resourceValues(forKeys: [.isSymbolicLinkKey,.fileSizeKey])
                        guard info.isSymbolicLink != true, Int64(info.fileSize ?? -1) == entry.size else { throw ClipboardError.changed }
                        let file=try FileHandle(forReadingFrom: entry.url); defer { try? file.close() }
                        var digest=SHA256(), count: Int64=0
                        while let bytes=try file.read(upToCount: 196608), !bytes.isEmpty {
                            try Task.checkCancellation(); count += Int64(bytes.count); guard count <= entry.size else { throw ClipboardError.changed }
                            digest.update(data: bytes); var data=ClipboardPacket("data",id: id); data.bytes=bytes; try await send(data)
                        }
                        guard count == entry.size else { throw ClipboardError.changed }
                        var end=ClipboardPacket("fileEnd",id: id); end.hash=Data(digest.finalize()).hex; try await send(end)
                    }
                }
                try await send(ClipboardPacket("commit",id: id)); await status("文件已发送 · 等待远端剪贴板")
            } else if let text {
                var packet=ClipboardPacket("text",id: id); packet.text=text; try packet.validate(); try await send(packet)
            }
        } catch {
            // The replacement transfer is queued after this cancellation on the same ordered stream.
            if !closed { try? await sendUncancelled(ClipboardPacket("cancel",id: id)) }
            if !(error is CancellationError) { await status(error.localizedDescription) }
        }
    }
    private func sendUncancelled(_ packet: ClipboardPacket) async throws {
        guard !closed, let secure=socket.secure else { return }
        try await socket.sendData(secure.sealData(JSONEncoder().encode(packet)))
    }
}

final class ClipboardBridge {
    var onStatus: ((String)->Void)?
    private let testMode: Bool
    private let testCache: URL?
    init(testMode: Bool = false) { self.testMode=testMode; self.testCache=testMode ? FileManager.default.temporaryDirectory.appendingPathComponent("ListFXFlow-clipboard-test-"+UUID().uuidString) : nil }
    private var listeners=[NWListener]()
    private var connection: NWConnection?
    private var session: ClipboardSession?
    private var timer: Timer?
    private var task: Task<Void,Never>?
    private var generation=0
    private var lastCount=0
    private var sessionEpoch=UUID()
    private var authenticated=false
    func start(bridges: [BridgeAddress], port: UInt16 = 49236, announce: @escaping (Data)->Void) throws {
        stop(); let token=Data.random(32), epoch=UUID(); sessionEpoch=epoch
        var announced=false
        for address in bridges {
            let tcp=NWProtocolTCP.Options(); tcp.noDelay=true
            let parameters=NWParameters(tls: nil,tcp: tcp); parameters.allowLocalEndpointReuse=true
            parameters.requiredLocalEndpoint = .hostPort(host: .init(address.address),port: NWEndpoint.Port(rawValue: port)!)
            if address.name != "lo0" { parameters.prohibitedInterfaceTypes=[.wifi,.cellular,.loopback] }
            let listener=try NWListener(using: parameters)
            listener.stateUpdateHandler={ [weak self] state in
                guard let self, self.sessionEpoch == epoch else { return }
                if case .ready=state, !announced { announced=true; announce(token) }
                if case .failed(let error)=state { self.onStatus?("剪贴板监听失败：\(error.localizedDescription)") }
            }
            listener.newConnectionHandler={ [weak self] incoming in
                guard let self, self.connection == nil else { incoming.cancel(); return }
                self.connection=incoming; self.authenticated=false
                incoming.start(queue: DispatchQueue(label: "ListFXFlow.clipboard.network"))
                let session=ClipboardSession(connection: incoming,secret: token,echo: self.testMode,cache: self.testCache,apply: { [weak self] result, generation in
                    guard let self else { return false }; return await MainActor.run { self.apply(result,generation: generation,epoch: epoch) }
                },status: { [weak self] status in guard let self else { return }; await MainActor.run { self.report(status,epoch: epoch) } },ready: { [weak self] in
                    guard let self else { return }; await MainActor.run { self.didConnect(epoch: epoch) }
                },ended: { [weak self,weak incoming] in
                    guard let self, let incoming else { return }; await MainActor.run { guard self.sessionEpoch == epoch, self.connection === incoming else { return }; self.timer?.invalidate(); self.timer=nil; self.connection=nil; self.session=nil }
                })
                self.session=session; self.task=Task { await session.run() }
                // Bound unauthenticated connection attempts; no silent second peer is admitted.
                DispatchQueue.main.asyncAfter(deadline: .now()+5) { [weak self,weak incoming] in
                    guard let self, self.sessionEpoch == epoch, self.connection === incoming, !self.authenticated else { return }; incoming?.cancel(); self.connection=nil
                }
            }
            listeners.append(listener); listener.start(queue: .main)
        }
        onStatus?("剪贴板连接中…")
    }
    private func report(_ value: String,epoch: UUID) { guard epoch == sessionEpoch else { return }; onStatus?(value) }
    private func didConnect(epoch: UUID) {
        guard epoch == sessionEpoch else { return }; authenticated=true
        guard !testMode else { return }
        lastCount=NSPasteboard.general.changeCount-1
        timer=Timer.scheduledTimer(withTimeInterval: 0.3,repeats: true) { [weak self] _ in self?.poll() }
        poll()
    }
    private func poll() {
        let board=NSPasteboard.general
        guard board.changeCount != lastCount, let session else { return }
        if #available(macOS 15.4, *), board.accessBehavior == .alwaysDeny {
            onStatus?("请在系统设置的剪贴板权限中允许 ListFXFlow")
            return
        }
        lastCount=board.changeCount; generation += 1
        let urls=(board.readObjects(forClasses: [NSURL.self],options: [.urlReadingFileURLsOnly:true]) as? [URL])
        let text=urls?.isEmpty == false ? nil : board.string(forType: .string)
        let current=generation
        Task { await session.localChanged(text: text,urls: urls?.isEmpty == false ? urls : nil,generation: current) }
    }
    private func apply(_ result: ClipboardResult,generation: Int,epoch: UUID) -> Bool {
        guard epoch == sessionEpoch, generation == self.generation, NSPasteboard.general.changeCount == lastCount else { return false }
        let board=NSPasteboard.general
        board.clearContents()
        let written: Bool
        switch result {
        case .text(let text): written=board.setString(text,forType: .string)
        case .files(let urls): written=board.writeObjects(urls.map { $0 as NSURL })
        }
        lastCount=board.changeCount; return written
    }
    func stop() {
        sessionEpoch=UUID(); authenticated=false; timer?.invalidate(); timer=nil; task?.cancel(); task=nil
        connection?.cancel(); connection=nil
        if let session { Task { await session.close() } }; session=nil
        for listener in listeners { listener.cancel() }; listeners=[]; generation=0
        onStatus?("剪贴板未连接")
    }
}
