using System.Runtime.InteropServices;

namespace ListFXFlow;

/// Ordinary mouse cursor cover only. Touchpad capture has its own process,
/// input window, lifecycle and SDK backend.
internal sealed class CursorShield : IDisposable
{
    private readonly Native.WindowProcedure procedure;
    public Action<Native.RawMouseInput,uint>? OnMouse;
    private IntPtr handle;
    private ushort windowClass;
    private bool registered,visible;
    private readonly string className="ListFXFlow.Cursor."+Guid.NewGuid().ToString("N");
    public CursorShield() {
        procedure=WindowProc;
        var c=new Native.WindowClass { Size=(uint)Marshal.SizeOf<Native.WindowClass>(),Instance=Native.GetModuleHandle(null),Procedure=Marshal.GetFunctionPointerForDelegate(procedure),ClassName=className,Background=Native.GetStockObject(4) };
        windowClass=Native.RegisterClassEx(ref c);
        if(windowClass==0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE; alpha 1 preserves cursor hit testing.
        handle=Native.CreateWindowEx(0x08080080,className,"",0x80000000,0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1),IntPtr.Zero,IntPtr.Zero,c.Instance,IntPtr.Zero);
        if(handle==IntPtr.Zero) { Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        if(!Native.SetLayeredWindowAttributes(handle,0,1,2)) { int error=Marshal.GetLastWin32Error(); Dispose(); throw new System.ComponentModel.Win32Exception(error); }
        registered=Native.RegisterRawInputDevices([new() { Page=1,Usage=2,Flags=0x100,Target=handle }],1,(uint)Marshal.SizeOf<Native.RawInputDevice>());
        if(!registered) { int error=Marshal.GetLastWin32Error(); Dispose(); throw new System.ComponentModel.Win32Exception(error); }
    }
    public void Show() {
        Native.SetWindowPos(handle,(IntPtr)(-1),0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1),0x50);
        // Swallowed low-level movement produces no WM_SETCURSOR. Hide immediately.
        Native.SetCursor(IntPtr.Zero);
        visible=true;
    }
    public void Hide() {
        if(!visible) return;
        visible=false;
        Native.SetCursor(Native.LoadCursor(IntPtr.Zero,(IntPtr)32512));
        if(handle!=IntPtr.Zero) Native.ShowWindow(handle,0);
    }
    private IntPtr WindowProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam) {
        if(message==0xff) {
            uint headerSize=(uint)Marshal.SizeOf<Native.RawInputHeader>(),size=headerSize;
            size=(uint)Marshal.SizeOf<Native.RawMouseInput>();
            uint read=Native.GetRawInputData(lParam,0x10000003,out var data,ref size,headerSize);
            if(read==size && read>=Marshal.SizeOf<Native.RawMouseInput>() && data.Header.Type==0 && data.Mouse.Extra!=(uint)Native.Marker)
                OnMouse?.Invoke(data,unchecked((uint)Native.GetMessageTime()));
            // DefWindowProc performs the foreground raw-input cleanup.
        }
        if(message==0x20 && visible) { Native.SetCursor(IntPtr.Zero); return (IntPtr)1; }
        if(message==0x21) return (IntPtr)3; // MA_NOACTIVATE
        return Native.DefWindowProc(window,message,wParam,lParam);
    }
    public void Dispose() {
        Hide();
        if(registered) { Native.RegisterRawInputDevices([new() { Page=1,Usage=2,Flags=1 }],1,(uint)Marshal.SizeOf<Native.RawInputDevice>()); registered=false; }
        if(handle!=IntPtr.Zero) { Native.DestroyWindow(handle); handle=IntPtr.Zero; }
        if(windowClass!=0) { Native.UnregisterClass(className,Native.GetModuleHandle(null)); windowClass=0; }
    }
}
