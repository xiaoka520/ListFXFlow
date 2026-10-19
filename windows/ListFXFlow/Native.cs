using System.Runtime.InteropServices;

namespace ListFXFlow;

internal static class Native
{
    internal const nuint Marker=0x4c465846;
    internal delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    internal delegate IntPtr WindowProcedure(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct WindowClass {
        public uint Size,Style; public IntPtr Procedure; public int ClassExtra,WindowExtra;
        public IntPtr Instance,Icon,Cursor,Background; public string? MenuName; public string ClassName; public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; internal readonly CaptureRect Value=>new(Left,Top,Right,Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct CursorInfo { public uint Size,Flags;public nint Cursor;public Point Position; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public uint Size;public Rect Monitor,Work;public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputDevice { public ushort Page,Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputHeader { public uint Type,Size; public IntPtr Device,WParam; }
    [StructLayout(LayoutKind.Explicit,Size=24)] internal struct RawMouse {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(12)] public int X;
        [FieldOffset(16)] public int Y;
        [FieldOffset(20)] public uint Extra;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawMouseInput { public RawInputHeader Header; public RawMouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHook { public Point Point; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardHook { public uint Vk,Scan,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Vk,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Union; }
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int hook,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code,uint mapType);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr CreateWindowEx(uint extendedStyle,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetLayeredWindowAttributes(IntPtr window,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] internal static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr LoadCursor(IntPtr instance,IntPtr name);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] internal static extern bool GetClipCursor(out Rect rectangle);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window,out Rect rectangle);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] internal static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint source,uint target,bool attach);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] internal static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall,SetLastError=true)] private delegate int RegisterTouchpadWindow(nint window,int enable);
    private static RegisterTouchpadWindow? touchpadWindow;
    [DllImport("kernel32.dll",EntryPoint="GetProcAddress",ExactSpelling=true)] private static extern nint GetExportOrdinal(nint module,nint ordinal);
    internal static bool RegisterTouchpadCapableWindow(nint window,bool enable) {
        if(touchpadWindow==null) {
            var module=GetModuleHandle("user32.dll");
            if(module==0)throw new NotSupportedException("Windows 触控板窗口接口不可用。");
            if(!NativeLibrary.TryGetExport(module,"RegisterTouchpadCapableWindow",out var entry))entry=GetExportOrdinal(module,(nint)2689);
            if(entry==0)throw new NotSupportedException("此 Windows 未提供触控板窗口接管接口。");
            touchpadWindow=Marshal.GetDelegateForFunctionPointer<RegisterTouchpadWindow>(entry);
        }
        return touchpadWindow(window,enable ? 1:0)!=0;
    }
    [DllImport("user32.dll")] internal static extern bool GetPointerType(uint pointer,out uint type);
    [DllImport("user32.dll")] internal static extern bool CloseGestureInfoHandle(nint gesture);
    [DllImport("user32.dll")] internal static extern int GetMessageTime();
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] internal static extern uint GetTickCount();
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterRawInputDevices(RawInputDevice[] devices,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint GetRawInputData(IntPtr input,uint command,out RawMouseInput data,ref uint size,uint headerSize);
    [DllImport("user32.dll",EntryPoint="GetRawInputData",SetLastError=true)] internal static extern uint GetRawInputHeader(IntPtr input,uint command,out RawInputHeader data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool UnregisterClass(string className,IntPtr instance);
    [DllImport("gdi32.dll")] internal static extern IntPtr GetStockObject(int index);
}
