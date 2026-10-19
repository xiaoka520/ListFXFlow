using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ListFXFlow;

public sealed record BridgeAdapter(string Name,IPAddress Address,IPAddress Mask,bool Thunderbolt)
{
    public override string ToString() => $"{(Thunderbolt ? "[雷雳] " : "")}{Name} · {Address}";
    public static BridgeAdapter[] All() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType==NetworkInterfaceType.Ethernet)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily==AddressFamily.InterNetwork)
            .Select(a => new BridgeAdapter(n.Description,a.Address,a.IPv4Mask,new[]{"thunderbolt","usb4","p2p"}.Any(s => (n.Name+" "+n.Description).Contains(s,StringComparison.OrdinalIgnoreCase)))))
        .OrderByDescending(a => a.Thunderbolt).ToArray();
    public bool SameSubnet(IPAddress other) {
        var a=Address.GetAddressBytes(); var b=other.GetAddressBytes(); var m=Mask.GetAddressBytes();
        return b.Length==4 && Enumerable.Range(0,4).All(i => (a[i]&m[i])==(b[i]&m[i]));
    }
    public IPAddress Broadcast { get { var a=Address.GetAddressBytes(); var m=Mask.GetAddressBytes(); return new IPAddress(Enumerable.Range(0,4).Select(i => (byte)(a[i]|~m[i])).ToArray()); } }
}

public static class ConnectionTarget
{
    public static IPEndPoint? Preferred(BridgeAdapter adapter,string? configured,string? lastAddress) {
        string? value=string.IsNullOrWhiteSpace(configured) ? lastAddress:configured;
        if(string.IsNullOrWhiteSpace(value))return null;
        // A saved address from the previous subnet must not block discovery.
        if(IPAddress.TryParse(value.Trim(),out var parsed) && parsed.AddressFamily==AddressFamily.InterNetwork && !adapter.SameSubnet(parsed))return null;
        try { return Resolve(adapter,value); }
        catch(ArgumentException) when(string.IsNullOrWhiteSpace(configured)) { return null; }
    }
    public static IPEndPoint? Resolve(BridgeAdapter adapter,string? value)
    {
        if(string.IsNullOrWhiteSpace(value)) return null;
        string text=value.Trim(); var pieces=text.Split('.');
        if(pieces.Length!=4 || pieces.Any(p=>!byte.TryParse(p,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out _)))
            throw new ArgumentException("请输入完整的 Mac IPv4 地址，例如 10.0.0.2；留空则自动发现。");
        var address=new IPAddress(pieces.Select(p=>byte.Parse(p,System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        byte first=address.GetAddressBytes()[0];
        if(first is 0 or >=224 || address.Equals(adapter.Broadcast) || (!IPAddress.IsLoopback(adapter.Address) && (IPAddress.IsLoopback(address) || address.Equals(adapter.Address))))
            throw new ArgumentException("请填写 Mac 的雷雳 IP，而不是本机、广播或回环地址。");
        if(!adapter.SameSubnet(address)) throw new ArgumentException("Mac IP 与所选网卡不在同一子网，请检查 IP 或选择正确的雷雳网卡。");
        return new IPEndPoint(address,49234);
    }
}

public sealed class FlowClient
{
    public Action<Message>? OnMessage;
    public Action<bool,string>? OnStatus;
    private readonly int port,discoveryPort;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Channel<Message> outgoing=Channel.CreateBounded<Message>(new BoundedChannelOptions(512) { SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait });
    private TcpClient? tcp;
    private SecureChannel? secure;
    private long lastReceived;
    private volatile bool connected;
    public IPAddress? PeerAddress { get; private set; }
    private string stopReason="连接已断开 · 已恢复本机控制";
    private readonly Func<ECDiffieHellman> identityProvider;
    private readonly Func<byte[]?> loadPeer;
    private readonly Action<byte[]> savePeer;
    public FlowClient(Func<ECDiffieHellman>? identityProvider=null,Func<byte[]?>? loadPeer=null,Action<byte[]>? savePeer=null,int port=49234,int discoveryPort=49235) {
        this.port=port; this.discoveryPort=discoveryPort;
        this.identityProvider=identityProvider??IdentityStore.Identity;
        this.loadPeer=loadPeer??(()=>IdentityStore.Read("peer"));
        this.savePeer=savePeer??(peer=>IdentityStore.Write("peer",peer));
    }

    public async Task Connect(BridgeAdapter adapter,string? macAddress=null,string? lastMacAddress=null)
    {
        try {
            var endpoint=ConnectionTarget.Preferred(adapter,macAddress,lastMacAddress);
            OnStatus?.Invoke(false,endpoint==null ? "正在雷雳网卡上查找 Mac…" : $"正在连接 Mac · {endpoint.Address}…");
            if(endpoint!=null) endpoint=new IPEndPoint(endpoint.Address,port);
            if(endpoint!=null) {
                tcp=new TcpClient(new IPEndPoint(adapter.Address,0)) { NoDelay=true };
                using var preferredTimeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);preferredTimeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
                try { await tcp.ConnectAsync(endpoint.Address,endpoint.Port,preferredTimeout.Token); }
                catch(Exception error) when(!lifetime.IsCancellationRequested && error is SocketException or OperationCanceledException) {
                    tcp.Dispose();tcp=null;endpoint=null;
                    OnStatus?.Invoke(false,"上次地址不可用 · 正在雷雳网卡上查找 Mac…");
                }
            }
            endpoint??=await Discover(adapter,lifetime.Token,port,discoveryPort);
            PeerAddress=endpoint.Address;
            using var handshake=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); handshake.CancelAfter(TimeSpan.FromSeconds(5));
            if(tcp==null) {
                tcp=new TcpClient(new IPEndPoint(adapter.Address,0)) { NoDelay=true };
                await tcp.ConnectAsync(endpoint.Address,endpoint.Port,handshake.Token);
            }
            var stream=tcp.GetStream(); using var identity=identityProvider();
            byte[] client=RandomNumberGenerator.GetBytes(32);
            await Wire.WriteFrame(stream,Wire.Encode(new("hello") { Version=1,Nonce=Convert.ToHexString(client).ToLowerInvariant(),PublicKey=Convert.ToHexString(Wire.Public(identity)).ToLowerInvariant() }),handshake.Token);
            var challenge=Wire.Decode(await Wire.ReadFrame(stream,handshake.Token));
            if(challenge.Type!="challenge" || challenge.Version!=1) throw new InvalidDataException("不兼容的设备版本");
            byte[] server=Convert.FromHexString(challenge.Nonce??""),peer=Convert.FromHexString(challenge.PublicKey??"");
            if(server.Length!=32 || peer.Length!=65) throw new InvalidDataException("设备身份无效");
            var trusted=loadPeer();
            if(trusted!=null && !CryptographicOperations.FixedTimeEquals(trusted,peer)) throw new InvalidDataException("Mac 的身份已变化。确认换机后需清理本机保存的配对信息。");
            byte[] secret=Wire.SharedSecret(identity,peer);
            try {
                if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(challenge.Auth??""),Wire.Proof(secret,"server",client,server))) throw new CryptographicException("Mac 身份验证失败");
                await Wire.WriteFrame(stream,Wire.Encode(new("finish") { Auth=Convert.ToHexString(Wire.Proof(secret,"client",client,server)).ToLowerInvariant() }),handshake.Token);
                secure=new SecureChannel(secret,client,server,false);
            } finally { CryptographicOperations.ZeroMemory(secret); }
            var first=secure.Open(await Wire.ReadFrame(stream,handshake.Token));
            if(first.Type!="ping") throw new InvalidDataException("握手未完成");
            savePeer(peer); connected=true; lastReceived=Environment.TickCount64;
            OnStatus?.Invoke(true,$"已连接 Mac · {endpoint.Address} · 加密雷雳直连");
            var reader=ReadLoop(stream); var writer=WriteLoop(stream); var heartbeat=Heartbeat();
            var completed=await Task.WhenAny(reader,writer,heartbeat);
            if(completed.Exception!=null && !lifetime.IsCancellationRequested) stopReason=completed.Exception.GetBaseException().Message;
            lifetime.Cancel(); tcp.Close();
            await Task.WhenAll(reader,writer,heartbeat);
        } catch(OperationCanceledException) {
            if(!lifetime.IsCancellationRequested) stopReason="未找到 Mac，或连接超时。请确认 Mac 已启用共享、两端网桥可互通。";
        } catch(Exception error) { if(!lifetime.IsCancellationRequested) stopReason=error.Message; }
        finally {
            connected=false; lifetime.Cancel(); tcp?.Close(); secure?.Dispose();
            OnStatus?.Invoke(false,stopReason);
        }
    }
    public void Send(Message m) { if(connected && !outgoing.Writer.TryWrite(m)) Stop("连接拥塞 · 已恢复本机控制"); }
    public void Stop(string reason="共享已停止 · 本机控制") { stopReason=reason; connected=false; lifetime.Cancel(); tcp?.Close(); }
    private async Task ReadLoop(NetworkStream stream) {
        while(!lifetime.IsCancellationRequested) {
            var m=secure!.Open(await Wire.ReadFrame(stream,lifetime.Token)); lastReceived=Environment.TickCount64; OnMessage?.Invoke(m);
        }
    }
    private async Task WriteLoop(NetworkStream stream) {
        await foreach(var m in outgoing.Reader.ReadAllAsync(lifetime.Token)) await Wire.WriteFrame(stream,secure!.Seal(m),lifetime.Token);
    }
    private async Task Heartbeat() {
        while(!lifetime.IsCancellationRequested) {
            await Task.Delay(1000,lifetime.Token);
            if(Environment.TickCount64-lastReceived>3000) throw new IOException("雷雳连接超时 · 已恢复本机控制");
            Send(new("ping"));
        }
    }
    private static async Task<IPEndPoint> Discover(BridgeAdapter adapter,CancellationToken token,int port,int discoveryPort)
    {
        using var udp=new UdpClient(new IPEndPoint(adapter.Address,0)) { EnableBroadcast=true };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] probe=Encoding.UTF8.GetBytes("ListFXFlow.discover.v1");
        var receive=udp.ReceiveAsync(deadline.Token).AsTask();
        while(!deadline.IsCancellationRequested) {
            await udp.SendAsync(probe,new IPEndPoint(IPAddress.IsLoopback(adapter.Address) ? IPAddress.Loopback : adapter.Broadcast,discoveryPort),deadline.Token);
            var completed=await Task.WhenAny(receive,Task.Delay(600,deadline.Token));
            if(completed!=receive) continue;
            var response=await receive;
            try {
                using var json=JsonDocument.Parse(response.Buffer); var root=json.RootElement;
                if(root.GetProperty("service").GetString()=="ListFXFlow/1" && root.GetProperty("port").GetInt32()==port &&
                    IPAddress.TryParse(root.GetProperty("address").GetString(),out var address) &&
                    address.Equals(response.RemoteEndPoint.Address) && adapter.SameSubnet(address)) return new IPEndPoint(address,port);
            } catch(JsonException) {} catch(KeyNotFoundException) {} catch(InvalidOperationException) {}
            receive=udp.ReceiveAsync(deadline.Token).AsTask();
        }
        throw new TimeoutException("没有找到已启用共享的 Mac，请检查雷雳网桥。");
    }
}
