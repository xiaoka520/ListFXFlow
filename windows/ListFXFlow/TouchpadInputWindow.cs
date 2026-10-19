using System.Runtime.InteropServices;

namespace ListFXFlow;

/// Foreground window for the touchpad worker only. It never registers raw mouse
/// input or runs the parent's mouse/keyboard hooks.
internal sealed class TouchpadInputWindow : IDisposable {
    private readonly Native.WindowProcedure procedure;
    private readonly string className="ListFXFlow.Touchpad."+Guid.NewGuid().ToString("N");
    private ushort atom;
    private nint handle;
    private bool registered;
    private readonly TouchpadFocusLease focus;
    internal Action<uint>? Pointer;
    internal Action? Cancel;
    internal bool ConsumePointers;
    internal bool Active {get;private set;}
    internal TouchpadInputWindow() {
        procedure=WindowProc;
        var c=new Native.WindowClass {Size=(uint)Marshal.SizeOf<Native.WindowClass>(),Instance=Native.GetModuleHandle(null),Procedure=Marshal.GetFunctionPointerForDelegate(procedure),ClassName=className};
        atom=Native.RegisterClassEx(ref c);
        if(atom==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        handle=Native.CreateWindowEx(0x80080,className,"",0x80000000,0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1),0,0,c.Instance,0);
        if(handle==0) { Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        focus=new(handle,Native.GetForegroundWindow,Native.IsWindow,Activate);
        if(!Native.SetLayeredWindowAttributes(handle,0,1,2)) { Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    }
    private static bool Activate(nint window)=>Native.GetForegroundWindow()==window || Native.SetForegroundWindow(window);
    internal void Prepare() {
        if(Active)return;
        if(registered)return;
        if(!Native.RegisterTouchpadCapableWindow(handle,true))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        registered=true;
        if(!Native.SetWindowPos(handle,(nint)(-1),0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1),0x50))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    internal bool TryAcquire() {
        if(Active && Foreground)return true;
        Prepare();
        bool acquired=focus.Acquire();
        TouchpadDiagnostics.Write("worker",$"foreground activation requested; confirmed={acquired}; {ForegroundDescription}");
        return Confirm();
    }
    internal bool Confirm() {
        Active=focus.Confirm();
        if(Active)Native.SetCursor(0);
        return Active;
    }
    internal string ForegroundDescription { get { var foreground=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(foreground,out uint pid);return $"foreground hwnd=0x{foreground:X} pid={pid}; input hwnd=0x{handle:X} pid={Environment.ProcessId}"; } }
    internal bool Foreground {
        get { Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint pid);return Active && pid==(uint)Environment.ProcessId; }
    }
    private nint WindowProc(nint window,uint message,nint wParam,nint lParam) {
        if(Active) {
            if(ConsumePointers && message is 0x245 or 0x246 or 0x247 or 0x24e or 0x24f)return 0;
            if(message is 0x245 or 0x246 or 0x247 && Native.GetPointerType(unchecked((uint)(long)wParam)&0xffff,out uint type) && type==5) {
                Pointer?.Invoke(unchecked((uint)(long)wParam)&0xffff);return 0;
            }
            if(message==0x24c) { Cancel?.Invoke();return 0; }
            if(message is 0x20a or 0x20e or 0x319)return 0;
            if(message==0x119) { Native.CloseGestureInfoHandle(lParam);return 0; }
            if(message==0x20) { Native.SetCursor(0);return 1; }
        }
        if(message==0x21)return 1;
        return Native.DefWindowProc(window,message,wParam,lParam);
    }
    internal void Release() {
        Active=false;
        if(registered && handle!=0) { Native.RegisterTouchpadCapableWindow(handle,false);registered=false; }
        focus?.Release();
        if(handle!=0)Native.ShowWindow(handle,0);
    }
    public void Dispose() {
        Release();
        if(handle!=0) { Native.DestroyWindow(handle);handle=0; }
        if(atom!=0) { Native.UnregisterClass(className,Native.GetModuleHandle(null));atom=0; }
        GC.KeepAlive(procedure);
    }
}
