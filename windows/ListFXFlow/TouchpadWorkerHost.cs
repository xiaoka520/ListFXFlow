using System.Diagnostics;
using System.IO.Pipes;
using System.Windows.Threading;
using System.Runtime.Versioning;

namespace ListFXFlow;

/// All experimental touchpad API calls live in this child process. The parent
/// can end it without waiting for this dispatcher or a native API to return.
[SupportedOSPlatform("windows10.0.26100.0")]
internal sealed class TouchpadWorkerHost : IDisposable {
    private readonly Dispatcher dispatcher;
    private readonly CancellationTokenSource stop=new();
    private readonly TouchpadWorkerQueue outgoing=new(),commands=new();
    private TouchpadSender sender=new();
    private NativeTouchpadInjector? injector;
    private readonly DispatcherTimer heartbeat;
    private TouchpadCaptureSession? capture;
    private long epoch;
    private int scheduled,errorSent;
    private bool remote,suppressOnly,forwardGestures;
    private readonly TouchpadActivationRetry activation=new();
    private readonly TouchpadFeedbackCounter feedback=new("synthetic-touchpad");
    private TouchpadWorkerHost(Dispatcher dispatcher) {
        this.dispatcher=dispatcher;
        heartbeat=new DispatcherTimer(TimeSpan.FromMilliseconds(250),DispatcherPriority.Background,(_,_)=> {
            try { if(remote)CheckForeground(); }
            catch(Exception error) when(TouchpadCaptureErrors.Recoverable(error)) { ReportError("检查原生手势接管失败："+error.Message);return; }
            var busy=injector?.BusySince ?? 0;
            if(busy!=0 && Environment.TickCount64-busy>1500) { ReportError("Windows 原生手势注入超时。");return; }
            Emit(new("heartbeat",epoch));
        },dispatcher);
        heartbeat.Start();
    }
    internal static async Task Run(string pipeName,int parentId,Dispatcher dispatcher) {
        using var host=new TouchpadWorkerHost(dispatcher);
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        using var parent=Process.GetProcessById(parentId);
        using var parentWatch=new System.Threading.Timer(_=> {
            try { if(parent.HasExited)Environment.Exit(0); } catch(InvalidOperationException) { Environment.Exit(0); }
        },null,500,500);
        TouchpadDiagnostics.Write("worker","connecting IPC; "+TouchpadDiagnostics.Runtime);
        await pipe.ConnectAsync(5000,host.stop.Token);
        host.Emit(new("ready"));
        async Task Read() {
            while(true) {
                var packet=await TouchpadWorkerWire.Read(pipe,host.stop.Token);
                if(packet.Kind=="stop")return;
                if(packet.Kind=="configure")host.commands.Replace(packet);
                else if(!host.commands.Offer(packet))throw new InvalidOperationException("Touchpad native command queue overflow");
                if(Interlocked.Exchange(ref host.scheduled,1)==0)_=dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(host.Drain));
            }
        }
        async Task Write() {
            while(true)await TouchpadWorkerWire.Write(pipe,await host.outgoing.TakeAsync(host.stop.Token),host.stop.Token);
        }
        var read=Task.Run(Read);var write=Task.Run(Write);var completed=await Task.WhenAny(read,write);
        host.stop.Cancel();
        try { await completed; } catch(OperationCanceledException) { }
        finally { try { await Task.WhenAll(read,write); } catch(Exception error) { TouchpadDiagnostics.Write("worker","IPC stopped",error); } }
    }
    private void Emit(TouchpadWorkerPacket packet) {
        if(!outgoing.Offer(packet))ReportError("手势输出队列拥塞。");
    }
    private void ReportError(string message) {
        if(Interlocked.Exchange(ref errorSent,1)!=0)return;
        TouchpadDiagnostics.Write("worker",message);
        outgoing.Replace(new("error",Interlocked.Read(ref epoch),Status:message[..Math.Min(message.Length,1024)]));
        dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=> {
            remote=false;activation.Stop();
            // Never destroy/disable a controller from its own native callback.
            capture?.Release();sender.Cancel();injector?.Reset();
        }));
    }
    private void ReportReadError(string message) {
        if(suppressOnly && !forwardGestures)return;
        TouchpadDiagnostics.Write("worker",message);
        if(sender.Cancel() is {} cancel)Emit(new("input",epoch,Input:cancel));
        Emit(new("captureWarning",epoch,Owned:capture?.Armed==true,Status:message[..Math.Min(message.Length,1024)]));
    }
    private void Drain() {
        try {
            for(int i=0;i<32 && commands.TryTake(out var packet);i++) {
                if(errorSent!=0)continue;
                if(packet!.Kind=="configure")Configure(packet);
                else if(packet.Epoch==epoch) {
                    if(packet.Kind=="activate" && remote && activation.Accepts(packet.Epoch)) {
                        if(capture?.TryAcquire()==true)CaptureReady();
                    }
                    if(packet.Kind=="reset")injector?.Reset();
                    if(packet.Kind=="input" && !remote && !suppressOnly && packet.Input is {} input)GetInjector().Post(input);
                }
            }
        } catch(Exception error) when(error is not AccessViolationException) { ReportError(error.ToString()); }
        finally {
            Interlocked.Exchange(ref scheduled,0);
            if(commands.Count>0 && Interlocked.Exchange(ref scheduled,1)==0)dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(Drain));
        }
    }
    private void Configure(TouchpadWorkerPacket packet) {
        TouchpadDiagnostics.Write("worker",$"configure remote={packet.Remote}; suppressOnly={packet.SuppressOnly}; forwardGestures={packet.ForwardGestures}");
        if(sender.Cancel() is {} cancel)Emit(new("input",epoch,Input:cancel));
        sender=new();
        if(suppressOnly!=packet.SuppressOnly || forwardGestures!=packet.ForwardGestures) { capture?.Dispose();capture=null; }
        suppressOnly=packet.SuppressOnly;forwardGestures=packet.ForwardGestures;
        activation.Stop();epoch=packet.Epoch;injector?.Reset();remote=packet.Remote;
        if(remote) {
            Emit(new("state",epoch,Status:suppressOnly ? "正在屏蔽本机多指手势…":"正在准备 SDK 触控板捕获…"));
            capture??=new TouchpadCaptureSession(dispatcher,(contacts,sourceTime)=> {
                if((!suppressOnly || forwardGestures) && sender.Sample(contacts,remote && capture?.Armed==true,minimumContacts:suppressOnly ? 3:2) is {} input) {
                    input.GestureEpoch=epoch;input.GestureTime=sourceTime;Emit(new("input",epoch,Input:input));
                }
            },ReportReadError,status=>Emit(new("state",epoch,Owned:capture?.Armed==true,Status:status)),suppressOnly,scroll=> {
                if(suppressOnly && remote && capture?.Armed==true)Emit(new("scroll",epoch,Input:scroll));
            },forwardGestures);
            capture.PrepareAcquire();
            activation.Begin(epoch,Environment.TickCount64);RequestForeground();
        } else {
            capture?.Release();
            Emit(new("state",epoch,Status:suppressOnly ? "本机手势正常 · 控制 Mac 时自动屏蔽":"已就绪 · 跨屏时接管 Windows 本机手势"));
        }
        TouchpadDiagnostics.Write("worker","configuration complete");
    }
    private void CaptureReady() {
        activation.Stop();
        Emit(new("state",epoch,Owned:true,Status:suppressOnly && !forwardGestures ? "本机多指手势已屏蔽":"本机多指手势已屏蔽 · "+capture!.SourceDescription+" · 等待多指触点"));
    }
    private void RequestForeground() {
        if(!activation.Request(Environment.TickCount64))return;
        TouchpadDiagnostics.Write("worker",$"request parent foreground grant epoch={epoch}; {capture?.ForegroundDescription}");
        Emit(new("state",epoch,Status:"等待 Windows 允许手势窗口激活…"));
        Emit(new("foregroundRequest",epoch));
    }
    private void CheckForeground() {
        if(capture==null)return;
        if(activation.Pending) {
            // SetForegroundWindow may complete after it returns. Let the message
            // pump run and confirm the actual foreground before reporting Owned.
            if(capture.ConfirmAcquire()) { CaptureReady();return; }
            if(activation.Expired(Environment.TickCount64)) {
                activation.Stop();capture.Release();
                TouchpadDiagnostics.Write("worker","foreground activation denied after bounded retries; "+capture.ForegroundDescription);
                Emit(new("state",epoch,Status:suppressOnly ? "手势屏蔽暂不可用 · 点击重试或返回本机后重新跨屏":"Windows 暂未允许手势接管 · 返回本机后重新跨屏可重试"));
                return;
            }
            RequestForeground();return;
        }
        if(capture.Active && !capture.Armed) {
            TouchpadDiagnostics.Write("worker","foreground ownership changed; "+capture.ForegroundDescription);
            if(sender.Cancel() is {} cancel)Emit(new("input",epoch,Input:cancel));
            capture.PrepareAcquire();
            activation.Begin(epoch,Environment.TickCount64);RequestForeground();
        }
    }
    private NativeTouchpadInjector GetInjector()=>injector??=new NativeTouchpadInjector(ReportError,(input,count)=> {
        if(feedback.Applied(input,count,Environment.TickCount64) is {} status)Emit(new("feedback",Interlocked.Read(ref epoch),Input:status));
    });
    public void Dispose() {
        activation.Stop();stop.Cancel();heartbeat.Stop();capture?.Dispose();injector?.Dispose();stop.Dispose();
        TouchpadDiagnostics.Write("worker","disposed");
    }
}
