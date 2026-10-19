using System.Diagnostics;

namespace ListFXFlow;

internal readonly record struct InjectionWork(Native.Input Input,bool Pointer=false,bool SetCursor=false,int X=0,int Y=0,long QueuedAt=0,bool Relative=false)
{
    public bool Wheel=>Input.Type==0 && (Input.Union.Mouse.Flags is 0x800 or 0x1000);
}

/// The capture-hook thread never calls blocking cursor/input APIs. This worker owns
/// native injection; pending motion is bounded and coalesced across wheel samples,
/// while keys/buttons remain ordering barriers (especially releases on reset).
internal sealed class InjectionWorker : IDisposable
{
    private readonly object gate=new();
    private readonly LinkedList<InjectionWork> pending=[];
    private readonly Thread thread;
    private readonly Action<InjectionWork> execute;
    private readonly Action<Exception>? failed;
    private bool stopping;
    private long count,coalesced,maxQueueTicks,maxCallTicks;
    internal InjectionWorker(Action<InjectionWork> execute,Action<Exception>? failed=null) {
        this.execute=execute;this.failed=failed;
        thread=new Thread(Run) { IsBackground=true,Name="ListFXFlow native injection" };
        thread.Start();
    }
    internal bool Post(InjectionWork work) {
        work=work with {QueuedAt=Stopwatch.GetTimestamp()};
        lock(gate) {
            if(stopping) return false;
            if(work.Relative && pending.Last is {} relative && relative.Value.Relative) {
                var input=work.Input;
                long dx=(long)input.Union.Mouse.X+relative.Value.Input.Union.Mouse.X,dy=(long)input.Union.Mouse.Y+relative.Value.Input.Union.Mouse.Y;
                if(Math.Abs(dx)<=1000000 && Math.Abs(dy)<=1000000) {
                    input.Union.Mouse.X=(int)dx;input.Union.Mouse.Y=(int)dy;
                    relative.Value=work with {Input=input,QueuedAt=relative.Value.QueuedAt};coalesced++;return true;
                }
            }
            if(work.Pointer) {
                for(var n=pending.Last;n!=null;n=n.Previous) {
                    if(n.Value.Pointer) { n.Value=work;coalesced++;return true; }
                    if(!n.Value.Wheel) break;
                }
            }
            if(work.Wheel && pending.Last is { } tail && tail.Value.Wheel && tail.Value.Input.Union.Mouse.Flags==work.Input.Union.Mouse.Flags) {
                int old=unchecked((int)tail.Value.Input.Union.Mouse.Data),delta=unchecked((int)work.Input.Union.Mouse.Data);
                if(Math.Sign(old)==Math.Sign(delta) && Math.Abs((long)old+delta)<=60000) {
                    var input=work.Input;input.Union.Mouse.Data=unchecked((uint)(old+delta));
                    tail.Value=work with {Input=input,QueuedAt=tail.Value.QueuedAt};coalesced++;return true;
                }
            }
            if(pending.Count>=512) return false;
            pending.AddLast(work);Monitor.Pulse(gate);return true;
        }
    }
    internal void CancelPending() {
        lock(gate) {
            for(var n=pending.First;n!=null;) {
                var next=n.Next;
                // Lock transitions are global, independent of pointer ownership.
                if(!(n.Value.Input.Type==1 && n.Value.Input.Union.Key.Scan==58)) pending.Remove(n);
                n=next;
            }
        }
    }
    internal void CancelPointer() { lock(gate) { for(var n=pending.First;n!=null;) { var next=n.Next;if(n.Value.Pointer || n.Value.Relative)pending.Remove(n);n=next; } } }
    internal (long Count,long Coalesced,double MaxQueueMs,double MaxCallMs) Statistics {
        get { lock(gate) return (count,coalesced,maxQueueTicks*1000.0/Stopwatch.Frequency,maxCallTicks*1000.0/Stopwatch.Frequency); }
    }
    private void Run() {
        while(true) {
            InjectionWork work;
            lock(gate) {
                while(pending.Count==0 && !stopping) Monitor.Wait(gate);
                if(pending.Count==0 && stopping)return;
                work=pending.First!.Value;pending.RemoveFirst();
            }
            long began=Stopwatch.GetTimestamp();
            try { execute(work); } catch(Exception error) { failed?.Invoke(error); }
            long ended=Stopwatch.GetTimestamp();
            lock(gate) { count++;maxQueueTicks=Math.Max(maxQueueTicks,began-work.QueuedAt);maxCallTicks=Math.Max(maxCallTicks,ended-began); }
        }
    }
    public void Dispose() {
        lock(gate) { stopping=true;Monitor.PulseAll(gate); }
        // Releases already queued by InputEngine.Stop drain before the worker exits.
        thread.Join(2000);
    }
}
