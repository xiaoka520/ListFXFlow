using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ListFXFlow;

public sealed class InputEngine : IDisposable
{
    private PointerRouter Router { get; }=new();
    public Action<Topology>? OnLayout;
    public Action<Message>? OnControl;
    private readonly Thread inputThread;
    private Dispatcher dispatcher=null!;
    private readonly InputMessageQueue queue=new();
    private int drainScheduled;
    private volatile bool disposed;
    public Action<Message>? Send;
    public Action? Emergency;
    public Action<string>? OnFocus;
    public Action? InjectionFailed;
    public Action<string>? OnTouchpadStatus;
    private readonly TouchpadWorkerClient touchpadWorker;
    private int touchpadDrainScheduled;
    private string? touchpadCaptureError,touchpadWorkerStatus;
    public string[] Features=>["clipboard-v1","caps-v1","game-mode-v1"];
    private readonly GameProtection gameProtection=new();
    private readonly GameShortcut gameShortcut=new();
    private DispatcherTimer gameTimer=null!;
    private bool peerGameMode,ownershipReady;
    private long lastGameProbe;
    private string gamePhase="off",gameStatus="";
    public Action<bool,string,bool>? OnGameProtection;
    public void SetAutomaticGameProtection(bool value) { if(!disposed)dispatcher.Invoke(()=> { gameProtection.AutomaticEnabled=value;RefreshGameProtection(); }); }
    public void ToggleGameProtection() { if(!disposed)dispatcher.Invoke(()=>SetManualGameProtection(!gameProtection.Locked)); }
    private void SetManualGameProtection(bool value) {
        if(!enabled || !peerGameMode || !ownershipReady || value && Router.Active!="windows")return;
        gameProtection.SetManual(value);ApplyGameProtection();
    }
    private void RefreshGameProtection(bool forceProbe=false) {
        bool canProbe=enabled && peerGameMode && ownershipReady && Router.Active=="windows" && gameProtection.AutomaticEnabled;
        long now=Environment.TickCount64;
        if(!canProbe)gameProtection.Observe(0);
        else if(forceProbe || now-lastGameProbe>=50) { lastGameProbe=now;gameProtection.Observe(GameCaptureProbe.Read(touchpadWorker.ProcessId)); }
        ApplyGameProtection();
    }
    private void ApplyGameProtection(bool force=false) {
        bool locked=enabled && peerGameMode && ownershipReady && Router.Active=="windows" && gameProtection.Locked;
        Router.CrossingLocked=locked;
        string phase=locked ? gameProtection.Phase:"off";
        if(force || phase!=gamePhase) {
            if(!locked && Router.Active=="windows" && gamePhase!="off" && Native.GetCursorPos(out var current))
                Router.Move("windows",current.X,current.Y,0,0,Width,Height,mayCross:false);
            gamePhase=phase;
            // Pending absolute samples must not warp a game after relative mode starts.
            pendingPointer=null;injector.CancelPointer();
            relativeRemainderX=relativeRemainderY=0;
            if(enabled && peerGameMode) {
                var r=Router.Topology.Windows;var p=r.Clamp(Router.Position);
                Send?.Invoke(new("gameMode") {Down=locked,Phase=phase,Active="windows",X=(p.X-r.X)/r.Width,Y=(p.Y-r.Y)/r.Height});
            }
        }
        string status=!enabled ? "连接后可用":locked ? (phase=="manual" ? "已手动锁定 Windows":"自动保护中 · 检测到隐藏指针窗口"):
            Router.Active!="windows" ? "控制 Windows 时可锁定":gameProtection.AutomaticEnabled ? "自动检测中 · 可手动锁定":"自动检测已关闭 · 可手动锁定";
        if(enabled && !peerGameMode)status+=" · 更新 Mac 可同步两端";
        if(force || status!=gameStatus) { gameStatus=status;OnGameProtection?.Invoke(locked,status,enabled && peerGameMode && ownershipReady && Router.Active=="windows"); }
    }
    private double relativeRemainderX,relativeRemainderY;
    public void RetryTouchpadSuppression() {
        if(!disposed)dispatcher.Invoke(()=> {
            touchpadWorker.Configure(false,false,true);touchpadWorker.Retry();
            touchpadCaptureError=null;touchpadWorkerStatus=null;
            RefreshTouchpadOwnership();UpdateTouchpadStatus();
        });
    }
    private void RefreshTouchpadOwnership() {
        bool wanted=TouchpadSuppressionPolicy.Wanted(enabled,Router.Active);
        touchpadWorker.Configure(enabled,wanted,suppressOnly:true);
    }
    private void UpdateTouchpadStatus()=>OnTouchpadStatus?.Invoke(!enabled ? "共享已停止 · 本机手势正常" :
        Router.Active=="windows" ? "本机手势正常 · 控制 Mac 时自动屏蔽" :
        touchpadCaptureError ?? touchpadWorkerStatus ?? "正在准备屏蔽本机多指手势…");
    private void ScheduleTouchpadDrain() {
        if(!disposed && Interlocked.Exchange(ref touchpadDrainScheduled,1)==0)dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(DrainTouchpad));
    }
    private void DrainTouchpad() {
        Interlocked.Exchange(ref touchpadDrainScheduled,0);
        for(int i=0;i<64 && touchpadWorker.TryTake(out var packet);i++) {
            if(packet!.Kind=="foregroundRequest") {
                if(TouchpadSuppressionPolicy.Wanted(enabled,Router.Active))touchpadWorker.GrantForeground(packet.Epoch);
            } else if(packet.Kind is "error" or "captureWarning") {
                touchpadCaptureError=packet.Status;UpdateTouchpadStatus();
            } else if(packet.Kind=="state") {
                touchpadCaptureError=null;touchpadWorkerStatus=packet.Status;UpdateTouchpadStatus();
            }
            // Forwarding is hidden. Contacts and injection receipts never cross
            // the network even if an older worker unexpectedly produces them.
        }
        if(touchpadWorker.HasPending)ScheduleTouchpadDrain();
    }
    private readonly Native.Hook mouseCallback,keyboardCallback;
    private IntPtr mouseHook,keyboardHook;
    private readonly HashSet<int> physicalKeys=[],physicalButtons=[],injectedKeys=[],injectedButtons=[];
    private Native.Point previous;
    private readonly RawMouseMotion rawMotion=new();
    private uint remoteSince;
    private readonly WheelTranslator wheel=new();
    private readonly InputClock frameClock;
    private readonly InjectionWorker injector;
    private readonly System.Threading.Timer performanceTimer;
    private int framePulsePending;
    private Message? pendingPointer;
    private Message? outgoingPointer;
    private double outgoingScrollX,outgoingScrollY;

    private bool enabled,injectionFailure,hasLocalPointer,capsSync,capsState,capsKeyHeld;
    private sealed record PointerEcho(int X,int Y,long At);
    private volatile PointerEcho? pointerEcho;
    private CursorShield? shield;
    public double Width=>Native.GetSystemMetrics(0);
    public double Height=>Native.GetSystemMetrics(1);
    public bool Enabled { set { if(!disposed) dispatcher.Invoke(()=>SetEnabled(value)); } }
    private void SetEnabled(bool value) {
        enabled=value;
        if(value) { touchpadWorker.Retry();touchpadCaptureError=null;touchpadWorkerStatus=null; }
        RefreshTouchpadOwnership();
        UpdateTouchpadStatus();
        if(!value) { queue.Clear(); Reset(); }
        else {
            injectionFailure=false; capsState=(Native.GetKeyState(0x14)&1)!=0;
            physicalKeys.Clear(); physicalButtons.Clear(); capsKeyHeld=false;
            foreach(var key in KeyMap.Entries) {
                int vk=(int)Native.MapVirtualKey((uint)(key.Scan | (key.Extended ? 0xe000 : 0)),3);
                if(key.Hid!=57 && vk!=0 && Native.GetAsyncKeyState(vk)<0) physicalKeys.Add(key.Hid);
            }
            int[] buttons=[1,2,4,5,6];
            for(int b=0;b<buttons.Length;b++) if(Native.GetAsyncKeyState(buttons[b])<0) physicalButtons.Add(b);
        }
    }
    public InputEngine() {
        mouseCallback=Mouse; keyboardCallback=Keyboard;
        using var ready=new ManualResetEventSlim();
        inputThread=new Thread(()=> {
            dispatcher=Dispatcher.CurrentDispatcher;
            Router.Topology.WindowsAspect=Width/Height;
            ready.Set(); Dispatcher.Run();
        }) { IsBackground=true,Name="ListFXFlow input" };
        inputThread.SetApartmentState(ApartmentState.STA); inputThread.Start(); ready.Wait();
        touchpadWorker=new TouchpadWorkerClient(ScheduleTouchpadDrain);
        injector=new InjectionWorker(InjectNative,error=>FailInjection());
        dispatcher.Invoke(()=> {
            gameTimer=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Background,(_,_)=>RefreshGameProtection(),dispatcher);
            gameTimer.Start();
        });
        performanceTimer=new System.Threading.Timer(_=>SavePerformance(),null,5000,5000);
        frameClock=new InputClock(()=> {
            if(disposed || Interlocked.Exchange(ref framePulsePending,1)!=0) return;
            // One pending frame callback keeps source samples and pointer work
            // bounded; received wheel deltas are handled directly by the input drain.
            dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=> {
                Interlocked.Exchange(ref framePulsePending,0);
                if(enabled) StepFrame(); else CancelFrame();
            }));
        });
    }
    public void Start() { ObjectDisposedException.ThrowIf(disposed,this); dispatcher.Invoke(StartCore); }
    private void StartCore() {
        if(mouseHook!=IntPtr.Zero) return;
        shield??=new CursorShield { OnMouse=RawMouse };
        Native.GetCursorPos(out previous);
        var module=Native.GetModuleHandle(null);
        mouseHook=Native.SetWindowsHookEx(14,mouseCallback,module,0);
        keyboardHook=Native.SetWindowsHookEx(13,keyboardCallback,module,0);
        if(mouseHook==IntPtr.Zero || keyboardHook==IntPtr.Zero) {
            int error=Marshal.GetLastWin32Error(); StopCore(); throw new System.ComponentModel.Win32Exception(error);
        }
    }
    public void ApplyTopology(Topology layout) {
        var copy=new Topology { Side=layout.Side,Offset=layout.Offset,MacAspect=layout.MacAspect,WindowsAspect=layout.WindowsAspect };
        if(!disposed) dispatcher.Invoke(()=> {
            Router.UpdateTopology(copy);
        });
    }
    private void PublishLayout() => OnLayout?.Invoke(new Topology { Side=Router.Topology.Side,Offset=Router.Topology.Offset,MacAspect=Router.Topology.MacAspect,WindowsAspect=Router.Topology.WindowsAspect });
    public void Receive(Message message) {
        if(disposed || message.Type=="ping") return;
        if(!queue.TryEnqueue(message)) { dispatcher.BeginInvoke(()=> { SetEnabled(false); Emergency?.Invoke(); }); return; }
        ScheduleDrain();
    }
    private void ScheduleDrain() {
        if(Interlocked.Exchange(ref drainScheduled,1)==0) dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(Drain));
    }
    private void Drain() {
        for(int i=0;i<64 && queue.TryDequeue(out var message);i++) if(enabled) ReceiveCore(message!);
        Interlocked.Exchange(ref drainScheduled,0);
        if(queue.HasPending) ScheduleDrain();
    }
    private IntPtr Keyboard(int code,IntPtr message,IntPtr data)
    {
        if(code<0 || !enabled) return Native.CallNextHookEx(keyboardHook,code,message,data);
        var k=Marshal.PtrToStructure<Native.KeyboardHook>(data);
        if(k.Extra==Native.Marker || (k.Flags&0x10)!=0) return Native.CallNextHookEx(keyboardHook,code,message,data);
        bool down=(k.Flags&0x80)==0;
        if(k.Scan==0x26 && (k.Flags&1)==0) {
            var shortcut=gameShortcut.Process(down,(physicalKeys.Contains(224) || physicalKeys.Contains(228)) &&
                (physicalKeys.Contains(226) || physicalKeys.Contains(230)),peerGameMode && ownershipReady && Router.Active=="windows");
            if(shortcut.Toggle)SetManualGameProtection(!gameProtection.Locked);
            if(shortcut.Consume)return (IntPtr)1;
        }
        if(down && k.Vk==0x1b && Native.GetAsyncKeyState(0x11)<0 && Native.GetAsyncKeyState(0x12)<0) {
            enabled=false; Reset(); Emergency?.Invoke(); return (IntPtr)1;
        }
        int scan=(int)k.Scan | ((k.Flags&1)!=0 ? 0x100 : 0);
        // Caps Lock is a toggle. Let Windows apply the physical press (so the local
        // LED follows) and broadcast the resulting state for the Mac to mirror.
        if(KeyMap.ByScan.TryGetValue(scan,out var caps) && caps.Hid==57) {
            FlushOutgoing();
            if(down && !capsKeyHeld) {
                capsKeyHeld=true;
                capsState=!capsState;
                if(capsSync) Send?.Invoke(new("caps") { Down=capsState });
            }
            if(!down) capsKeyHeld=false;
            return Native.CallNextHookEx(keyboardHook,code,message,data);
        }
        FinishWheel();
        if(KeyMap.ByScan.TryGetValue(scan,out var entry)) {
            if(down) physicalKeys.Add(entry.Hid); else physicalKeys.Remove(entry.Hid);
            if(Router.Active!="windows") { FlushOutgoing(); Send?.Invoke(new("key") { Key=entry.Hid,Down=down }); }
        }
        return Router.Active!="windows" ? (IntPtr)1 : Native.CallNextHookEx(keyboardHook,code,message,data);
    }
    private IntPtr Mouse(int code,IntPtr message,IntPtr data)
    {
        if(code<0) return Native.CallNextHookEx(mouseHook,code,message,data);
        var m=Marshal.PtrToStructure<Native.MouseHook>(data); int kind=message.ToInt32();
        if(m.Extra==Native.Marker || (m.Flags&1)!=0) { previous=m.Point; return Native.CallNextHookEx(mouseHook,code,message,data); }
        if(!enabled) { previous=m.Point; return Native.CallNextHookEx(mouseHook,code,message,data); }
        bool remote=Router.Active!="windows";
        // Keep the desktop pointer exactly where it left Windows. WM_INPUT supplies
        // movement, buttons and wheel in device order while these legacy events are blocked.
        if(remote) return (IntPtr)1;
        if(kind==0x200) {
            // SetCursorPos is used for hover movement. If it generates a low-level
            // event, do not feed that synthetic position back into the router.
            if(pointerEcho is { } echo && Environment.TickCount64-echo.At<=80 &&
               Math.Abs(m.Point.X-echo.X)<=1 && Math.Abs(m.Point.Y-echo.Y)<=1) {
                previous=m.Point;
                return Native.CallNextHookEx(mouseHook,code,message,data);
            }
            bool edge=m.Point.X<=1 || m.Point.Y<=1 || m.Point.X>=Width-2 || m.Point.Y>=Height-2;
            RefreshGameProtection(forceProbe:!Router.CrossingLocked && edge);
            if(Router.CrossingLocked) {
                // The game owns physical mouse movement, cursor hiding/warping
                // and raw input. Do not convert it into desktop edge crossings.
                previous=m.Point;
                return Native.CallNextHookEx(mouseHook,code,message,data);
            }
            pendingPointer=null; injector.CancelPointer();
            hasLocalPointer=true;
            double dx=m.Point.X-previous.X,dy=m.Point.Y-previous.Y;
            previous=m.Point; string old=Router.Active;
            var packet=Router.Move("windows",m.Point.X,m.Point.Y,dx,dy,Width,Height,physicalKeys.Count==0 && physicalButtons.Count==0);
            if(old!=Router.Active) ChangeFocus(m.Point,m.Time);
            // Crossings are ownership transitions and are never delayed by a frame.
            Send?.Invoke(packet);
            if(Router.Active!="windows") return (IntPtr)1;
        } else {
            int button=kind switch { 0x201 or 0x202=>0,0x204 or 0x205=>1,0x207 or 0x208=>2,0x20b or 0x20c=>(int)(m.Data>>16)+2,_=>-1 };
            if(button>=0) {
                FinishWheel();
                bool down=kind is 0x201 or 0x204 or 0x207 or 0x20b;
                if(down) physicalButtons.Add(button); else physicalButtons.Remove(button);
            }
        }
        return Native.CallNextHookEx(mouseHook,code,message,data);
    }
    private void RawMouse(Native.RawMouseInput input,uint time)
    {
        var mouse=input.Mouse;
        bool virtualDesktop=(mouse.Flags&2)!=0;
        var delta=rawMotion.Read(input.Header.Device,mouse.Flags,mouse.X,mouse.Y,
            virtualDesktop ? Native.GetSystemMetrics(78) : Width,virtualDesktop ? Native.GetSystemMetrics(79) : Height);
        if(!enabled || Router.Active=="windows") return;
        int elapsed=RawMouseMotion.Since(time,remoteSince);
        if(elapsed<0) return; // Ignore device messages queued before the focus transfer.
        if(elapsed>0 && (delta.X!=0 || delta.Y!=0)) {
            string old=Router.Active;
            var packet=Router.Move("windows",0,0,delta.X,delta.Y,Width,Height,physicalKeys.Count==0 && physicalButtons.Count==0);
            if(old!=Router.Active) {
                // Ownership changes are sent immediately; they also clear stale samples.
                ChangeFocus();
                Send?.Invoke(packet);
                if(Router.Active=="windows") { hasLocalPointer=true; InjectPointer(packet); }
            } else if(Router.Active!="windows") {
                // Down to one pointer packet per frame instead of every raw sample.
                outgoingPointer=packet; frameClock.Start();
            }
        }
        // A packet may contain movement and a click; deliver the position first.
        for(int button=0;button<5;button++) {
            int down=1<<(button*2),up=down<<1;
            if((mouse.ButtonFlags&down)!=0) RawButton(button,true);
            if((mouse.ButtonFlags&up)!=0) RawButton(button,false);
        }
        double amount=unchecked((short)mouse.ButtonData)/3.0;
        double dy=(mouse.ButtonFlags&0x400)!=0 ? amount : 0,dx=(mouse.ButtonFlags&0x800)!=0 ? -amount : 0;
        if((dx!=0 || dy!=0) && !TouchpadSuppressionPolicy.DropRawWheel(enabled,Router.Active,touchpadWorker.Owned,input.Header.Device)) {
            if(Router.Active!="windows") {
                outgoingScrollX+=dx; outgoingScrollY+=dy; frameClock.Start();
            }
            else { if(dy!=0) MouseEvent(0x800,unchecked((uint)(int)(dy*3))); if(dx!=0) MouseEvent(0x1000,unchecked((uint)(int)(-dx*3))); }
        }
    }
    private void RawButton(int button,bool down) {
        if(down) physicalButtons.Add(button); else physicalButtons.Remove(button);
        if(Router.Active!="windows") { FlushOutgoing(); Send?.Invoke(new("button") { Button=button,Down=down }); }
        else ButtonEvent(button,down); // The legacy event was suppressed before this return transition.
    }
    private void ReceiveCore(Message m)
    {
        if(m.Type is "key" or "button") FinishWheel();
        switch(m.Type) {
            case "display":
                peerGameMode=m.Features?.Contains("game-mode-v1")==true;ApplyGameProtection(force:true);
                capsSync=m.Features?.Contains("caps-v1")==true; Router.Topology.MacAspect=m.Width!.Value/m.Height!.Value; PublishLayout(); break;
            case "gameModeRequest" when peerGameMode: SetManualGameProtection(m.Down!.Value);break;
            case "relativePointer" when peerGameMode && Router.Active=="windows" && Router.CrossingLocked:
                InjectRelative(m.Dx!.Value,m.Dy!.Value);break;
            case "layout": Router.UpdateTopology(new Topology { Side=m.Side!,Offset=m.Offset!.Value,MacAspect=Router.Topology.MacAspect,WindowsAspect=Router.Topology.WindowsAspect }); PublishLayout(); break;
            case "pointer":
                bool initialPointer=!ownershipReady;
                ownershipReady=true;
                if(!initialPointer && Router.Active=="windows" && m.Active!="windows")RefreshGameProtection(forceProbe:true);
                string old=Router.Active; var from=Router.Position;
                if(!Router.Accept(m))break;
                if(initialPointer)ApplyGameProtection(force:true);
                if(old!=Router.Active) {
                    Native.Point? exit=null;
                    if(old=="windows" && hasLocalPointer && CursorDeparture.Project(Router.Topology.Windows,from,Router.Position,Width,Height) is { } p)
                        exit=new Native.Point { X=(int)Math.Round(p.X),Y=(int)Math.Round(p.Y) };
                    ChangeFocus(exit);
                }
                hasLocalPointer=Router.Active=="windows";
                if(hasLocalPointer && !Router.CrossingLocked) { if(old!=Router.Active) InjectPointer(m); else { pendingPointer=m; frameClock.Start(); } }
                break;
            case "key" when Router.Active=="windows": if(m.Down!.Value) injectedKeys.Add(m.Key!.Value); else injectedKeys.Remove(m.Key!.Value); KeyEvent(m.Key.Value,m.Down.Value); break;
            case "button" when Router.Active=="windows": if(m.Down!.Value) injectedButtons.Add(m.Button!.Value); else injectedButtons.Remove(m.Button!.Value); ButtonEvent(m.Button.Value,m.Down.Value); break;
            case "scroll" when Router.Active=="windows":
                // The source already coalesces wheel samples. Submit the real
                // delta once on arrival instead of waiting for a second timer.
                FlushPointer();
                wheel.Add(-m.Dx!.Value,m.Dy!.Value); StepWheel();
                break;
            case "caps": SetCaps(m.Down!.Value); break;
            case "reset": SetEnabled(false); OnControl?.Invoke(m); break;
        }
    }
    private void SetCaps(bool state) {
        if(capsState==state) return;
        capsState=state;
        KeyEvent(57,true); KeyEvent(57,false);
    }
    private void StepWheel() {
        var delta=wheel.Take();
        if(delta.Y!=0) MouseEvent(0x800,unchecked((uint)delta.Y));
        if(delta.X!=0) MouseEvent(0x1000,unchecked((uint)delta.X));
    }
    private void FlushPointer() { if(pendingPointer is { } pointer) { pendingPointer=null; if(enabled && Router.Active=="windows") InjectPointer(pointer); } }
    private void FlushOutgoing(bool includeScroll=true) {
        if(Router.Active=="windows") { outgoingPointer=null; outgoingScrollX=outgoingScrollY=0; return; }
        if(outgoingPointer is { } pointer) { outgoingPointer=null; Send?.Invoke(pointer); }
        if(includeScroll) FlushOutgoingScroll();
    }
    private void FlushOutgoingScroll() {
        if(outgoingScrollX!=0 || outgoingScrollY!=0) {
            var packet=new Message("scroll") { Dx=outgoingScrollX,Dy=outgoingScrollY };
            outgoingScrollX=outgoingScrollY=0; Send?.Invoke(packet);
        }
    }
    private bool HasPendingFrame => pendingPointer!=null || outgoingPointer!=null || outgoingScrollX!=0 || outgoingScrollY!=0 || wheel.Pending;
    private void StopClockIfIdle() { if(!HasPendingFrame) frameClock.Stop(); }
    private void StepFrame() {
        // Pointer is always injected first and at the full 250 Hz frame rate.
        FlushPointer();
        FlushOutgoing();
        StopClockIfIdle();
    }
    private void FinishWheel() {
        FlushPointer();
        if(wheel.Pending && enabled && Router.Active=="windows") StepWheel();
        StopClockIfIdle();
    }
    private void CancelFrame() {
        pendingPointer=null; outgoingPointer=null; outgoingScrollX=outgoingScrollY=0;
        wheel.Reset(); frameClock.Stop();
    }
    private void ChangeFocus(Native.Point? exitPoint=null,uint? time=null)
    {
        touchpadWorker.Retry();touchpadCaptureError=null;touchpadWorkerStatus=null;
        CancelFrame(); injector.CancelPending();
        ReleaseInjected();
        foreach(int key in physicalKeys) KeyEvent(key,false);
        foreach(int button in physicalButtons) ButtonEvent(button,false);
        physicalKeys.Clear(); physicalButtons.Clear();
        if(Router.Active!="windows") {
            remoteSince=time??Native.GetTickCount();
            // A locally intercepted crossing has not reached the system pointer yet.
            // Park once at the exit intersection; initial ownership keeps the current position.
            shield?.Show();
            if(exitPoint is { } point) Absolute(point.X,point.Y);
            else if(Native.GetCursorPos(out var current)) Absolute(current.X,current.Y);
        }
        else shield?.Hide();
        RefreshTouchpadOwnership();UpdateTouchpadStatus();
        RefreshGameProtection();
        OnFocus?.Invoke(Router.Active);
    }
    private void SavePerformance() {
        try {
            var stats=injector.Statistics;
            var directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ListFXFlow","Diagnostics");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"input-timing.json"),System.Text.Json.JsonSerializer.Serialize(new {
                capturedAt=DateTimeOffset.UtcNow,nativeCalls=stats.Count,coalescedSamples=stats.Coalesced,
                maxQueueMs=stats.MaxQueueMs,maxNativeCallMs=stats.MaxCallMs }));
        } catch(Exception error) when(error is System.IO.IOException or UnauthorizedAccessException) { }
    }
    private void FailInjection() {
        if(disposed) return;
        dispatcher.BeginInvoke(()=> {
            if(injectionFailure)return;
            injectionFailure=true;SetEnabled(false);InjectionFailed?.Invoke();
        });
    }
    private void Inject(Native.Input input) {
        if(!injector.Post(new InjectionWork(input))) FailInjection();
    }
    private void InjectNative(InjectionWork work) {
        if(work.Pointer) pointerEcho=new(work.X,work.Y,Environment.TickCount64);
        bool done=work.Pointer && work.SetCursor && Native.SetCursorPos(work.X,work.Y);
        if(!done && Native.SendInput(1,[work.Input],Marshal.SizeOf<Native.Input>())!=1) FailInjection();
    }
    private void KeyEvent(int hid,bool down)
    {
        if(!KeyMap.ByHid.TryGetValue(hid,out var key)) return;
        Inject(new Native.Input { Type=1,Union=new Native.InputUnion { Key=new Native.KeyInput { Scan=(ushort)key.Scan,Flags=8u | (key.Extended ? 1u : 0) | (down ? 0 : 2u),Extra=Native.Marker } } });
    }
    private void MouseEvent(uint flags,uint data=0,int x=0,int y=0) => Inject(new Native.Input { Type=0,Union=new Native.InputUnion { Mouse=new Native.MouseInput { X=x,Y=y,Data=data,Flags=flags,Extra=Native.Marker } } });
    private void ButtonEvent(int button,bool down) {
        uint flags=button switch { 0=>down ? 2u : 4u,1=>down ? 8u : 16u,2=>down ? 32u : 64u,_=>down ? 128u : 256u };
        MouseEvent(flags,button>=3 ? (uint)(button-2) : 0);
    }
    private Native.Input AbsoluteInput(double x,double y) {
        int vx=Native.GetSystemMetrics(76),vy=Native.GetSystemMetrics(77),vw=Native.GetSystemMetrics(78),vh=Native.GetSystemMetrics(79);
        return new Native.Input { Type=0,Union=new Native.InputUnion { Mouse=new Native.MouseInput {
            X=(int)Math.Clamp((x-vx)*65535/Math.Max(1,vw-1),0,65535),Y=(int)Math.Clamp((y-vy)*65535/Math.Max(1,vh-1),0,65535),
            Flags=0xc001,Extra=Native.Marker } } };
    }
    private void Absolute(double x,double y) {
        if(!injector.Post(new InjectionWork(AbsoluteInput(x,y),true,false,(int)x,(int)y))) FailInjection();
    }
    private void InjectPointer(Message m) {
        int x=(int)Math.Round(m.X!.Value*(Width-1)),y=(int)Math.Round(m.Y!.Value*(Height-1));
        // SendInput / SetCursorPos run on a different thread from low-level hooks.
        // Windows can deliver our hook callbacks while a native call is in flight.
        if(!injector.Post(new InjectionWork(AbsoluteInput(x,y),true,injectedButtons.Count==0,x,y))) FailInjection();
    }
    private void InjectRelative(double dx,double dy) {
        relativeRemainderX+=dx;relativeRemainderY+=dy;
        int x=(int)Math.Truncate(relativeRemainderX),y=(int)Math.Truncate(relativeRemainderY);
        relativeRemainderX-=x;relativeRemainderY-=y;
        if(x==0 && y==0)return;
        var input=new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {X=x,Y=y,Flags=1,Extra=Native.Marker}}};
        if(!injector.Post(new InjectionWork(input,Relative:true)))FailInjection();
    }
    private void ReleaseInjected() {
        foreach(int key in injectedKeys) KeyEvent(key,false);
        foreach(int button in injectedButtons) ButtonEvent(button,false);
        injectedKeys.Clear(); injectedButtons.Clear();
    }
    private void Reset() { gameProtection.Reset();gameShortcut.Reset();peerGameMode=false;ownershipReady=false;gamePhase="off";relativeRemainderX=relativeRemainderY=0;touchpadCaptureError=null;touchpadWorkerStatus=null;touchpadWorker.Configure(false,false,true);injector.CancelPending(); ReleaseInjected(); CancelFrame(); Router.Reset("windows");ApplyGameProtection(force:true); hasLocalPointer=false; shield?.Hide(); physicalKeys.Clear(); physicalButtons.Clear(); OnFocus?.Invoke("windows"); }
    private void StopCore() {
        SetEnabled(false);
        if(mouseHook!=IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
        if(keyboardHook!=IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
        mouseHook=keyboardHook=IntPtr.Zero;
        shield?.Dispose(); shield=null;
        rawMotion.Clear();
    }
    public void Stop() { if(!disposed) dispatcher.Invoke(StopCore); }
    public void Dispose() {
        if(disposed) return;
        performanceTimer.Dispose(); dispatcher.Invoke(()=> { gameTimer.Stop();StopCore(); }); frameClock.Dispose(); touchpadWorker.Dispose();injector.Dispose(); SavePerformance(); disposed=true;
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        inputThread.Join(1000);
    }
}
