using System.Runtime.Versioning;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Input;

namespace ListFXFlow;

/// Windows SDK projections own all WinRT ABI/event marshalling. This class owns
/// one capture lifetime in the isolated STA worker; no window enumeration,
/// subclassing, handcrafted delegate IIDs, or COM function-table calls.
[SupportedOSPlatform("windows10.0.26100.0")]
internal sealed class TouchpadCaptureSession : IDisposable {
    private readonly Dispatcher dispatcher;
    private readonly TouchpadInputWindow window;
    private readonly NativeTouchpadCapture twoFinger=new();
    private readonly TouchpadContactBatch batch=new();
    private readonly TouchpadGesturesController controller;
    private readonly Action<TouchpadContact[],double?> contacts;
    private readonly Action<string> warning;
    private readonly Action<string> activity;
    private readonly bool suppressOnly,forwardGestures;
    private PtpHidCapture? rawCapture;
    private readonly PtpHidStrokeGate rawStroke=new();
    private int rawDrainScheduled;
    internal string SourceDescription=>rawCapture?.Description ?? "SDK 触点";
    private void ScheduleRaw(long token) {
        if(Interlocked.Exchange(ref rawDrainScheduled,1)!=0)return;
        dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=> {
            Interlocked.Exchange(ref rawDrainScheduled,0);
            if(disposed)return;
            if(token!=generation) {if(rawCapture?.HasPending==true)ScheduleRaw(generation);return;}
            for(int i=0;i<32 && rawCapture!=null && rawCapture.TryRead(out var frame);i++)if(CanRead() && rawStroke.Accept(frame!.Contacts.Length))Deliver(frame.Contacts,frame.Time);
            if(rawCapture?.HasPending==true)ScheduleRaw(token);
        }));
    }
    private void RawFailed(long token,string message) {
        dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=> {
            if(disposed || token!=generation)return;
            // The host warning cancels the sender. A fabricated empty frame
            // would commit an "end" before cancellation and rearm mid-stroke.
            rawCapture?.Dispose();rawCapture=null;rawStroke.Reset();batch.Reset();warning(message);
        }));
    }
    private readonly ScrollDiagnostics scrollDiagnostics=new("scroll-worker");
    private bool active,disposed,flushQueued;
    private long generation;
    private string? readFailure;
    private long lastWarning;
    private const TouchpadGlobalGestureKinds All=(TouchpadGlobalGestureKinds)0x3f;
    internal bool Armed=>active && window.Foreground && controller.Enabled && (controller.SupportedGestures&All)==All;
    internal bool Active=>active;
    internal string ForegroundDescription=>window.ForegroundDescription;
    internal TouchpadCaptureSession(Dispatcher dispatcher,Action<TouchpadContact[],double?> contacts,Action<string> warning,Action<string> activity,bool suppressOnly=false,Action<Message>? scroll=null,bool forwardGestures=false) {
        this.dispatcher=dispatcher;this.contacts=contacts;this.warning=warning;this.activity=activity;
        this.suppressOnly=suppressOnly;this.forwardGestures=forwardGestures;
        TouchpadDiagnostics.Write("worker","SDK capture: checking API availability");
        if(!ApiInformation.IsTypePresent("Windows.UI.Input.TouchpadGesturesController") || !TouchpadGesturesController.IsSupported())throw new NotSupportedException("此 Windows 未提供原生触控板接管 API。");
        controller=TouchpadGesturesController.CreateForProcess();
        TouchpadDiagnostics.Write("worker","SDK capture: controller created");
        try {
            controller.PointerPressed+=OnGlobalPressed;controller.PointerMoved+=OnPoint;controller.PointerReleased+=OnPoint;
            controller.GlobalActionPerformed+=OnAction;
            controller.SupportedGestures=All;controller.Enabled=false;
            window=new() { Pointer=suppressOnly ? null:ReadTwoFinger,Cancel=suppressOnly ? null:CancelContacts,PromoteScroll=suppressOnly,
                CaptureTwoFingerPointers=TouchpadSuppressionPolicy.CaptureTwoFingerPointers(suppressOnly),
                PointerObserved=()=>scrollDiagnostics.Count(ScrollStage.Pointer),WheelObserved=()=>scrollDiagnostics.Count(ScrollStage.WindowWheel),
                Scroll=value=> { if(suppressOnly && CanRead()) { scrollDiagnostics.Count(ScrollStage.WorkerForwarded);scroll?.Invoke(value); } } };
            TouchpadDiagnostics.Write("worker","SDK capture: typed subscriptions and dedicated input window ready");
        } catch {
            try { controller.Enabled=false; } finally { Unsubscribe(); }
            throw;
        }
    }
    internal void PrepareAcquire() {
        ObjectDisposedException.ThrowIf(disposed,this);
        active=false;controller.Enabled=false;batch.Reset();twoFinger.Reset();generation++;flushQueued=false;
        rawCapture?.Dispose();rawCapture=null;rawStroke.Reset();
        if(suppressOnly && forwardGestures) {
            long token=generation;
            rawCapture=PtpHidCapture.TryOpen(()=>ScheduleRaw(token),message=>RawFailed(token,message),out string description);
            TouchpadDiagnostics.Write("worker",description);
        }
        if(!suppressOnly)twoFinger.Prepare();window.Prepare();
    }
    internal bool TryAcquire() {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Armed)return true;
        if(!window.TryAcquire())return false;
        return ConfirmAcquire();
    }
    internal bool ConfirmAcquire() {
        if(Armed)return true;
        if(!window.Confirm())return false;
        controller.Enabled=true;
        if(!controller.Enabled || (controller.SupportedGestures&All)!=All)throw new InvalidOperationException("原生触控板控制器未保持启用状态或手势范围。");
        active=true;readFailure=null;
        scrollDiagnostics.Context="armed; two-finger="+(suppressOnly ? "system-wheel":"native-pointers");
        TouchpadDiagnostics.Write("worker",$"SDK capture armed; foreground={window.Foreground}; enabled={controller.Enabled}; supported={controller.SupportedGestures}");
        return true;
    }
    private bool CanRead()=>active && !disposed && window.Foreground;
    private void OnGlobalPressed(TouchpadGesturesController sender,PointerEventArgs args) {
        if(CanRead())rawStroke.Pressed();
        OnPoint(sender,args);
    }
    private void OnPoint(TouchpadGesturesController sender,PointerEventArgs args) {
        scrollDiagnostics.Count(ScrollStage.GlobalPointer);
        // Read only SDK-owned global contacts; do not register the window for
        // two-finger pointers or steal the native wheel/pinch recognition path.
        args.Handled=true;
        if(suppressOnly && (!forwardGestures || rawCapture!=null))return;
        if(!CanRead())return;
        try {
            var point=args.CurrentPoint;var p=point.Properties;
            if(!point.IsPhysicalPositionSupported)throw new NotSupportedException("系统未提供触点的设备坐标。");
            var position=point.PhysicalPosition;var bounds=point.PointerDevice.PhysicalDeviceRect;
            if(bounds.Width<=0 || bounds.Height<=0)throw new InvalidOperationException("触控板物理尺寸无效。");
            var previous=batch.Push(new(point.PointerId,point.FrameId,point.IsInContact && p.TouchConfidence,p.IsCanceled,
                (position.X-bounds.X)/bounds.Width,(position.Y-bounds.Y)/bounds.Height));
            if(previous!=null)Deliver(previous);
            if(!flushQueued) {
                flushQueued=true;long token=generation;
                dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=> {
                    if(token!=generation)return;flushQueued=false;
                    if(CanRead() && batch.Flush() is {} frame)Deliver(frame);
                }));
            }
        } catch(Exception error) when(error is not AccessViolationException) { ReadFailed("SDK PointerPoint",error); }
    }
    private void OnAction(TouchpadGesturesController sender,TouchpadGlobalActionEventArgs args) {
        if(suppressOnly)return;
        if(CanRead())activity("已捕获本机多指动作 · "+args.Action);
    }
    private void ReadTwoFinger(uint id) {
        if(!CanRead())return;
        try { if(twoFinger.Read(id,"two-finger input window") is {} frame)Deliver(frame); }
        catch(Exception error) when(error is not AccessViolationException) { ReadFailed("双指触点",error); }
    }
    private void Deliver(TouchpadContact[] frame,double? sourceTime=null) {
        if(readFailure!=null) { readFailure=null;activity("触点读取已恢复"); }
        contacts(frame,sourceTime);
    }
    private void ReadFailed(string stage,Exception error) {
        string message=stage+"："+error.Message;long now=Environment.TickCount64;
        batch.Reset();twoFinger.Reset();contacts([],null);
        if(readFailure!=message || now-lastWarning>=2000) {
            readFailure=message;lastWarning=now;warning(message);
        }
    }
    private void CancelContacts() { batch.Reset();twoFinger.Reset();if(active)contacts([],null); }
    internal void Release() {
        scrollDiagnostics.Context="released";
        active=false;generation++;flushQueued=false;batch.Reset();twoFinger.Reset();rawCapture?.Dispose();rawCapture=null;rawStroke.Reset();
        try { controller.Enabled=false; } finally { window?.Release(); }
    }
    public void Dispose() {
        if(disposed)return;disposed=true;
        try { Release(); } finally { try { Unsubscribe(); } finally { window?.Dispose();scrollDiagnostics.Dispose(); } }
    }
    private void Unsubscribe() {
        controller.PointerPressed-=OnGlobalPressed;controller.PointerMoved-=OnPoint;controller.PointerReleased-=OnPoint;controller.GlobalActionPerformed-=OnAction;
    }
}
