using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ListFXFlow;

public sealed class ClipboardLink : IDisposable
{
    private readonly CancellationTokenSource life=new();
    private readonly SemaphoreSlim writer=new(1,1);
    private readonly ClipboardInbox inbox;
    private TcpClient? tcp;
    private SecureChannel? secure;
    private NetworkStream? stream;
    private CancellationTokenSource? outbound;
    private string? outboundId;
    private bool outboundFiles;
    private long generation,inboundGeneration;
    private Task? sending;
    public Func<ClipboardResult,long,Task<bool>>? OnResult;
    public Action<string>? OnStatus;
    public Action? OnReady;
    public ClipboardLink(string cache) { inbox=new(cache); }
    public async Task Run(IPAddress local,IPAddress remote,byte[] secret,int port=49236)
    {
        try {
            tcp=new TcpClient(new IPEndPoint(local,0)) { NoDelay=true };
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(life.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(remote,port,deadline.Token); stream=tcp.GetStream();
            byte[] cn=RandomNumberGenerator.GetBytes(32);
            await Wire.WriteFrame(stream,Wire.Encode(new("hello") { Nonce=Convert.ToHexString(cn).ToLowerInvariant(),Auth=Convert.ToHexString(Wire.Proof(secret,"clipboard-client",cn,[])).ToLowerInvariant() }),deadline.Token);
            var challenge=Wire.Decode(await Wire.ReadFrame(stream,deadline.Token));
            byte[] sn=Convert.FromHexString(challenge.Nonce??"");
            if(challenge.Type!="challenge" || sn.Length!=32 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(challenge.Auth??""),Wire.Proof(secret,"clipboard-server",cn,sn))) throw new CryptographicException("剪贴板连接认证失败");
            await Wire.WriteFrame(stream,Wire.Encode(new("finish") { Auth=Convert.ToHexString(Wire.Proof(secret,"clipboard-finish",cn,sn)).ToLowerInvariant() }),deadline.Token);
            secure=new SecureChannel(SHA256.HashData([..Encoding.UTF8.GetBytes("ListFXFlow/Clipboard/1"),..secret]),cn,sn,false);
            OnReady?.Invoke(); OnStatus?.Invoke("剪贴板已连接");
            while(!life.IsCancellationRequested) {
                var packet=JsonSerializer.Deserialize<ClipboardPacket>(secure.OpenData(await Wire.ReadFrame(stream,life.Token,1048576)),Wire.Json)??throw new InvalidDataException();
                packet.Validate();
                if(packet.Kind=="done") { if(packet.Id==outboundId) OnStatus?.Invoke(outboundFiles ? "文件已同步 · 可在远端粘贴" : "文字剪贴板已就绪"); continue; }
                if(packet.Kind=="cancel" && packet.Id==outboundId) outbound?.Cancel();
                if(packet.Kind is "begin" or "text") inboundGeneration=Interlocked.Read(ref generation);
                try {
                    ClipboardResult? result; lock(inbox) result=inbox.Receive(packet);
                    if(result!=null) { bool applied=OnResult!=null && await OnResult(result,inboundGeneration); await Send(new(applied ? "done" : "cancel",packet.Id),life.Token); OnStatus?.Invoke(applied ? "剪贴板已就绪" : "本机剪贴板已变化，未覆盖新内容"); }
                    else if(packet.Kind=="begin") OnStatus?.Invoke("正在接收文件…");
                    else if(packet.Kind=="data" && inbox.Id==packet.Id && inbox.Received%(196608*8)<196608) OnStatus?.Invoke($"正在接收文件 · {inbox.Received/1024/1024} / {inbox.Total/1024/1024} MB");
                } catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException) {
                    lock(inbox) inbox.Abort(); await Send(new("cancel",packet.Id),life.Token); OnStatus?.Invoke("剪贴板："+error.Message);
                }
            }
        } catch(Exception error) {
            if(!life.IsCancellationRequested) OnStatus?.Invoke("剪贴板连接已停止："+error.Message);
        } finally {
            life.Cancel(); outbound?.Cancel(); tcp?.Close();
            if(sending!=null) { try { await sending; } catch(Exception) {} }
            lock(inbox) inbox.Abort(); secure?.Dispose(); secure=null;
        }
    }
    private async Task Send(ClipboardPacket packet,CancellationToken token) {
        await writer.WaitAsync(token);
        try {
            token.ThrowIfCancellationRequested(); if(secure==null || stream==null) throw new IOException("剪贴板未连接");
            byte[] bytes=secure.SealData(JsonSerializer.SerializeToUtf8Bytes(packet,Wire.Json));
            if(bytes.Length>1048576) throw new InvalidDataException("剪贴板消息过大");
            await Wire.WriteFrame(stream,bytes,life.Token);
        } finally { writer.Release(); }
    }
    public void LocalChanged(string? text,string[]? files,long version) {
        if(life.IsCancellationRequested) return;
        Interlocked.Exchange(ref generation,version);
        string? previous; lock(inbox) { previous=inbox.Id; inbox.Abort(); }
        var previousOutbound=outbound; previousOutbound?.Cancel(); var cancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token); outbound=cancel;
        string id=Guid.NewGuid().ToString("D"); outboundId=id; outboundFiles=files!=null;
        var prior=sending;
        sending=Task.Run(async()=> {
            // Finish cancelling the older revision before publishing the next one.
            if(prior!=null) { try { await prior; } catch(Exception) {} } previousOutbound?.Dispose();
            if(previous!=null) { try { await Send(new("cancel",previous),life.Token); } catch(Exception error) { if(!life.IsCancellationRequested) OnStatus?.Invoke(error.Message); } }
            await Publish(id,text,files,cancel.Token);
        });
    }
    private async Task Publish(string id,string? text,string[]? files,CancellationToken token) {
        try {
            token.ThrowIfCancellationRequested();
            if(files!=null) {
                var manifest=ClipboardManifest.Build(files,token);
                await Send(new("begin",id) { Roots=manifest.Roots,Count=manifest.Entries.Count,Total=manifest.Total },token);
                OnStatus?.Invoke("正在发送文件…");
                foreach(var entry in manifest.Entries) {
                    await Send(new("entry",id) { Path=entry.RelativePath,Size=entry.Size,Directory=entry.Directory },token);
                    if(entry.Directory) continue;
                    if((File.GetAttributes(entry.LocalPath)&FileAttributes.ReparsePoint)!=0) throw new IOException("源文件变成了链接，请重新复制");
                    await using var source=new FileStream(entry.LocalPath,FileMode.Open,FileAccess.Read,FileShare.Read,196608,true);
                    if(source.Length!=entry.Size) throw new IOException("源文件已变化，请重新复制");
                    using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer=new byte[196608]; long count=0;
                    int read;
                    while((read=await source.ReadAsync(buffer,token))>0) {
                        count+=read; if(count>entry.Size) throw new IOException("源文件已变化，请重新复制");
                        hash.AppendData(buffer,0,read); await Send(new("data",id) { Bytes=buffer[..read] },token);
                    }
                    if(count!=entry.Size) throw new IOException("源文件已变化，请重新复制");
                    await Send(new("fileEnd",id) { Hash=Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() },token);
                }
                await Send(new("commit",id),token); OnStatus?.Invoke("文件已发送 · 等待远端剪贴板");
            } else if(text!=null) { var packet=new ClipboardPacket("text",id) { Text=text }; packet.Validate(); await Send(packet,token); }
        } catch(Exception error) {
            if(!life.IsCancellationRequested) { try { await Send(new("cancel",id),life.Token); } catch(Exception e) when(e is IOException or OperationCanceledException or SocketException) {} }
            if(error is not OperationCanceledException) OnStatus?.Invoke("剪贴板："+error.Message);
        }
    }
    public void Dispose() { life.Cancel(); outbound?.Cancel(); tcp?.Close(); }
}
