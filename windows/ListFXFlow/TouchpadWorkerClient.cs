using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace ListFXFlow;

/// No native gesture calls, process waits or pipe I/O on the caller's input/UI
/// thread. A hung/crashed worker is stopped once; only an explicit retry clears it.
internal sealed class TouchpadWorkerClient : IDisposable {
    private sealed class Session(long epoch) {
        internal readonly CancellationTokenSource Stop=new();
        internal readonly TouchpadWorkerQueue Outgoing=new();
        internal readonly long InitialEpoch=epoch;
        internal long LastBeat=Environment.TickCount64;
        internal int ProcessId;
    }
    private readonly object gate=new();
    private readonly TouchpadWorkerQueue events=new();
    private readonly Action notify;
    private readonly Func<string,Process> launch;
    private readonly int timeoutMs;
    private readonly Func<int,bool> allowForeground;
    private Session? session;
    private long epoch;
    private bool active,remote,failed,disposed,suppressOnly,forwardGestures;
    internal bool Owned {get;private set;}
    internal int ProcessId { get { lock(gate)return session?.ProcessId ?? 0; } }
    internal long Epoch { get { lock(gate)return epoch; } }
    internal bool HasPending=>events.Count!=0;
    internal Task Completion {get;private set;}=Task.CompletedTask;
    internal TouchpadWorkerClient(Action notify,Func<string,Process>? launch=null,int timeoutMs=3000,Func<int,bool>? allowForeground=null) {
        this.notify=notify;this.launch=launch??Launch;this.timeoutMs=timeoutMs;
        this.allowForeground=allowForeground??(pid=>Native.AllowSetForegroundWindow((uint)pid));
    }
    private static Process Launch(string pipe) {
        var info=new ProcessStartInfo(Environment.ProcessPath??throw new IOException("Cannot find application executable")) {UseShellExecute=false,CreateNoWindow=true};
        info.ArgumentList.Add("--touchpad-worker");info.ArgumentList.Add(pipe);info.ArgumentList.Add(Environment.ProcessId.ToString());
        return Process.Start(info)??throw new IOException("Cannot start touchpad worker");
    }
    internal void Retry() { lock(gate)failed=false; }
    // Called on the parent's input dispatcher after the child is prepared.
    // Foreground eligibility is renewed for this child, never ASFW_ANY.
    internal void GrantForeground(long expectedEpoch) {
        Session? current;bool queued;
        lock(gate) {
            current=session;
            if(disposed || failed || !active || !remote || current==null || current.ProcessId==0 || epoch!=expectedEpoch)return;
            bool granted=allowForeground(current.ProcessId);
            int error=granted ? 0:System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            TouchpadDiagnostics.Write("main",$"foreground grant epoch={epoch}; child pid={current.ProcessId}; allowed={granted}; Win32={error}");
            queued=current.Outgoing.Offer(new("activate",epoch));
        }
        if(!queued)Fail(current,new IOException("Touchpad foreground command queue overflow"));
    }
    internal void Configure(bool enabled,bool remoteCapture,bool suppressOnly=false,bool forwardGestures=false) {
        lock(gate) {
            if(disposed || active==enabled && remote==remoteCapture && this.suppressOnly==suppressOnly && this.forwardGestures==forwardGestures && (session!=null || !enabled || failed))return;
            active=enabled;remote=remoteCapture;this.suppressOnly=suppressOnly;this.forwardGestures=forwardGestures;Owned=false;epoch++;events.Replace();
            if(!enabled) { session?.Stop.Cancel();return; }
            if(failed)return;
            var command=new TouchpadWorkerPacket("configure",epoch,remoteCapture,SuppressOnly:suppressOnly,ForwardGestures:forwardGestures);
            if(session==null) {
                var current=new Session(epoch);current.Outgoing.Replace(command);session=current;
                Completion=Task.Run(()=>Run(current));
            } else session.Outgoing.Replace(command);
        }
    }
    internal void Post(Message message) {
        Session? current;
        lock(gate) {
            current=session;
            if(disposed || failed || !active || remote || suppressOnly || current==null)return;
            if(current.Outgoing.Offer(new("input",epoch,Input:message)))return;
        }
        Fail(current,new IOException("Touchpad worker input queue overflow"));
    }
    internal void ResetInjection() { lock(gate) { if(active && session!=null && !failed)session.Outgoing.Offer(new("reset",epoch)); } }
    internal bool TryTake(out TouchpadWorkerPacket? packet) {
        lock(gate) {
            while(events.TryTake(out packet))if(packet!.Epoch==epoch)return true;
            packet=null;return false;
        }
    }
    private void Publish(Session current,TouchpadWorkerPacket packet) {
        bool wake=false;
        lock(gate) {
            if(session!=current || current.Stop.IsCancellationRequested || packet.Epoch!=epoch || !active)return;
            if(packet.Kind=="state")Owned=packet.Owned;
            if(packet.Kind is "heartbeat" or "ready")return;
            wake=events.Offer(packet);
        }
        if(wake)notify();else Fail(current,new IOException("Touchpad worker output queue overflow"));
    }
    private void Fail(Session current,Exception error) {
        lock(gate) {
            if(session!=current || failed || disposed || current.Stop.IsCancellationRequested)return;
            string reason=error.Message.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??error.GetType().Name;
            if(reason.Length>350)reason=reason[..350];
            failed=true;Owned=false;current.Stop.Cancel();events.Replace(new("error",epoch,Status:suppressOnly ? $"手势屏蔽暂不可用：{reason}；点击重试或重新跨屏。":$"手势已暂停：{reason}；键鼠共享继续，关闭再开启可重试。"));
        }
        TouchpadDiagnostics.Write("main","worker failure",error);notify();
    }
    private async Task Run(Session current) {
        Process? process=null;
        var pipeName="lfx-"+Guid.NewGuid().ToString("N");
        NamedPipeServerStream? pipe=null;
        var token=current.Stop.Token;
        try {
            TouchpadDiagnostics.Write("main","starting isolated worker; "+TouchpadDiagnostics.Runtime);
            pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            token.ThrowIfCancellationRequested();process=launch(pipeName);
            lock(gate)current.ProcessId=process.Id;
            using(var startup=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                startup.CancelAfter(timeoutMs*2);await pipe.WaitForConnectionAsync(startup.Token);
            }
            Interlocked.Exchange(ref current.LastBeat,Environment.TickCount64);
            async Task Read() {
                while(true) {
                    var packet=await TouchpadWorkerWire.Read(pipe,token);
                    Interlocked.Exchange(ref current.LastBeat,Environment.TickCount64);
                    if(packet.Kind=="error")throw new IOException(packet.Status??"Native gesture worker failed");
                    Publish(current,packet);
                }
            }
            async Task Write() {
                while(true)await TouchpadWorkerWire.Write(pipe,await current.Outgoing.TakeAsync(token),token);
            }
            async Task Watch() {
                while(true) {
                    await Task.Delay(Math.Min(100,timeoutMs),token);
                    if(process.HasExited)throw new IOException("Touchpad worker exited: "+process.ExitCode);
                    if(Environment.TickCount64-Interlocked.Read(ref current.LastBeat)>timeoutMs)throw new TimeoutException("Native touchpad worker stopped responding");
                }
            }
            var tasks=new[]{Read(),Write(),Watch()};var finished=await Task.WhenAny(tasks);
            try { await finished; }
            catch(Exception error) { if(!token.IsCancellationRequested)Fail(current,error); }
            finally { current.Stop.Cancel();try { await Task.WhenAll(tasks); } catch(Exception) { } }
        } catch(Exception error) when(error is not AccessViolationException) {
            if(!token.IsCancellationRequested)Fail(current,error);
        } finally {
            current.Stop.Cancel();
            if(process!=null) {
                try {
                    if(pipe?.IsConnected==true) { using var stop=new CancellationTokenSource(150);await TouchpadWorkerWire.Write(pipe,new("stop"),stop.Token); }
                } catch(Exception) { }
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(500)); }
                catch(TimeoutException) { try { process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch(Exception error) { TouchpadDiagnostics.Write("main","worker kill",error); } }
                catch(Exception error) { TouchpadDiagnostics.Write("main","worker exit",error); }
                try { if(process.HasExited)TouchpadDiagnostics.Write("main","worker exit code=0x"+process.ExitCode.ToString("X8")); } catch(InvalidOperationException) { }
                process.Dispose();
            }
            pipe?.Dispose();
            bool restart=false;
            lock(gate) {
                if(session==current) { session=null;Owned=false;restart=active && !failed && !disposed; }
            }
            if(restart)Configure(true,remote,suppressOnly);
            current.Stop.Dispose();
            TouchpadDiagnostics.Write("main","worker released");
        }
    }
    public void Dispose() { lock(gate) { disposed=true;active=false;Owned=false;session?.Stop.Cancel();events.Replace(); } }
}
