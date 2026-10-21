using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace ListFXFlow;

internal sealed record TouchpadWorkerPacket(string Kind,long Epoch=0,bool Remote=false,bool Owned=false,Message? Input=null,string? Status=null,bool SuppressOnly=false,bool ForwardGestures=false);

/// Bounded IPC buffering. Only consecutive updates to the same contacts merge;
/// begin/end/cancel and ownership messages remain ordering barriers.
internal sealed class TouchpadWorkerQueue(int capacity=128) {
    private readonly object gate=new();
    private readonly LinkedList<TouchpadWorkerPacket> packets=[];
    private readonly Channel<byte> signal=Channel.CreateBounded<byte>(new BoundedChannelOptions(1) {FullMode=BoundedChannelFullMode.DropWrite});
    internal int Count { get { lock(gate)return packets.Count; } }
    internal bool Offer(TouchpadWorkerPacket packet) {
        lock(gate) {
            if(packets.Last is {Value:var old} && old.Epoch==packet.Epoch && old.Kind==packet.Kind) {
                if(packet.Kind=="scroll" && packet.Input is {Type:"scroll"} nextScroll && old.Input is {Type:"scroll"} priorScroll && priorScroll.Down==nextScroll.Down &&
                   priorScroll.Dx!.Value*nextScroll.Dx!.Value>=0 && priorScroll.Dy!.Value*nextScroll.Dy!.Value>=0 &&
                   Math.Abs(priorScroll.Dx.Value+nextScroll.Dx.Value)<=10000 && Math.Abs(priorScroll.Dy.Value+nextScroll.Dy.Value)<=10000) {
                    priorScroll.Dx+=nextScroll.Dx;priorScroll.Dy+=nextScroll.Dy;return true;
                }
                if(packet.Kind=="heartbeat" || packet.Input is {Phase:"change"} next && old.Input is {Phase:"change"} prior && next.Contacts!.Select(c=>c.Id).SequenceEqual(prior.Contacts!.Select(c=>c.Id))) {
                    packets.Last.Value=packet;return true;
                }
            }
            if(packets.Count>=capacity)return false;
            packets.AddLast(packet);
        }
        signal.Writer.TryWrite(0);return true;
    }
    internal void Replace(TouchpadWorkerPacket? packet=null) {
        lock(gate) { packets.Clear();if(packet!=null)packets.AddLast(packet); }
        signal.Writer.TryWrite(0);
    }
    internal bool TryTake(out TouchpadWorkerPacket? packet) {
        lock(gate) {
            if(packets.First==null) { packet=null;return false; }
            packet=packets.First.Value;packets.RemoveFirst();return true;
        }
    }
    internal async Task<TouchpadWorkerPacket> TakeAsync(CancellationToken token) {
        while(true) { if(TryTake(out var packet))return packet!;await signal.Reader.ReadAsync(token); }
    }
}

internal static class TouchpadWorkerWire {
    internal static async Task Write(Stream stream,TouchpadWorkerPacket packet,CancellationToken token) =>
        await Wire.WriteFrame(stream,JsonSerializer.SerializeToUtf8Bytes(packet),token);
    internal static async Task<TouchpadWorkerPacket> Read(Stream stream,CancellationToken token) {
        byte[] header=new byte[4];await stream.ReadExactlyAsync(header,token);
        int count=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header);
        if(count is <=0 or >8192)throw new InvalidDataException("Invalid touchpad worker frame size");
        byte[] bytes=new byte[count];await stream.ReadExactlyAsync(bytes,token);
        var packet=JsonSerializer.Deserialize<TouchpadWorkerPacket>(bytes,new JsonSerializerOptions {MaxDepth=12})??throw new InvalidDataException("Empty worker packet");
        if(packet.Kind is not ("configure" or "activate" or "foregroundRequest" or "stop" or "reset" or "input" or "scroll" or "feedback" or "ready" or "state" or "heartbeat" or "error" or "captureWarning") || packet.Status?.Length>1024)throw new InvalidDataException("Invalid worker packet");
        if(packet.Kind=="scroll" && packet.Input==null)throw new InvalidDataException("Missing worker scroll");
        if(packet.Input is {} input) { if(input.Type!=(packet.Kind switch {"feedback"=>"touchpadFeedback","scroll"=>"scroll",_=>"touchpad"}))throw new InvalidDataException("Invalid worker input");input.Validate(); }
        return packet;
    }
}

internal static class TouchpadDiagnostics {
    internal static string? DirectoryOverride {get;set;}
    internal static string Runtime=>
        $"build={System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetEntryAssembly()!)?.InformationalVersion}; Windows={Environment.OSVersion.Version}";
    internal static void Write(string role,string stage,Exception? error=null) {
        try {
            var directory=DirectoryOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ListFXFlow","Diagnostics");Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,"touchpad-"+role+".log");
            lock(typeof(TouchpadDiagnostics)) {
                if(File.Exists(file) && new FileInfo(file).Length>262144)File.Move(file,file+".previous",true);
                File.AppendAllText(file,$"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {stage}{(error==null ? "":" "+error)}{Environment.NewLine}");
            }
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
}
