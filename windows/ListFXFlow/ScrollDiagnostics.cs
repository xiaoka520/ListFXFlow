namespace ListFXFlow;

internal enum ScrollStage { Pointer,GlobalPointer,WindowWheel,HookWheel,RawTouchpad,RawMouse,WorkerReceived,WorkerForwarded,WorkerDropped,SentFrame }

/// Counters only on input threads. File I/O runs on a background timer, never in a hook.
internal sealed class ScrollDiagnostics : IDisposable {
    private readonly long[] counts=new long[Enum.GetValues<ScrollStage>().Length];
    private readonly Action<string> write;
    private readonly System.Threading.Timer? timer;
    private readonly object gate=new();
    private string context="initial",last="";
    private bool disposed;
    internal string Context { set=>Volatile.Write(ref context,value); }
    internal ScrollDiagnostics(string role,Action<string>? write=null,bool automatic=true) {
        this.write=write??(value=>TouchpadDiagnostics.Write(role,value));
        if(automatic)timer=new System.Threading.Timer(_=>Flush(),null,1000,1000);
    }
    internal void Count(ScrollStage stage)=>Interlocked.Increment(ref counts[(int)stage]);
    internal void Flush() {
        lock(gate) {
            if(disposed)return;
            string snapshot=Volatile.Read(ref context)+" "+string.Join(" ",Enum.GetValues<ScrollStage>().Select(stage=>$"{stage}={Interlocked.Read(ref counts[(int)stage])}"));
            if(snapshot==last)return;
            write(snapshot);last=snapshot;
        }
    }
    public void Dispose() { timer?.Dispose();Flush();lock(gate)disposed=true; }
}
