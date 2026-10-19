using System.Runtime.InteropServices;

namespace ListFXFlow;

/// Contacts delivered by Windows after its touchpad disambiguation, not the
/// fixed mouse position in ptPixelLocation. Shared by WM_POINTER and WinRT.
internal sealed class TouchpadPointerFrames {
    private readonly Dictionary<uint,int> identities=[];
    private (nint Device,uint Frame,ulong Time)? previous;
    internal void Reset() { identities.Clear();previous=null; }
    internal TouchpadContact[]? Read(NativeTouchpadInjector.TouchInfo[] frame,NativeTouchpadInjector.Rect bounds) {
        if(frame.Length==0 || bounds.Right<=bounds.Left || bounds.Bottom<=bounds.Top)return null;
        var key=(frame[0].Pointer.Device,frame[0].Pointer.Frame,frame[0].Pointer.Performance);
        if(previous==key)return null;
        previous=key;
        if(frame.Any(p=>(p.Pointer.Flags&0x8000)!=0)) { identities.Clear();return []; }
        var live=frame.Where(p=>(p.Pointer.Flags&0x4004)==0x4004).ToArray(); // CONFIDENCE | INCONTACT
        if(live.Length>5 || live.Select(p=>p.Pointer.Id).Distinct().Count()!=live.Length) { identities.Clear();return []; }
        foreach(var old in identities.Keys.Where(id=>!live.Any(p=>p.Pointer.Id==id)).ToArray())identities.Remove(old);
        foreach(var p in live)if(!identities.ContainsKey(p.Pointer.Id))identities[p.Pointer.Id]=Enumerable.Range(0,5).First(i=>!identities.Values.Contains(i));
        return live.Select(p=>new TouchpadContact(identities[p.Pointer.Id],
            Math.Clamp((p.Pointer.Himetric.X-(double)bounds.Left)/(bounds.Right-(double)bounds.Left),0,1),
            Math.Clamp((p.Pointer.Himetric.Y-(double)bounds.Top)/(bounds.Bottom-(double)bounds.Top),0,1))).OrderBy(p=>p.Id).ToArray();
    }
}

internal sealed class NativeTouchpadCapture {
    [UnmanagedFunctionPointer(CallingConvention.StdCall,SetLastError=true)]
    private delegate int GetFrame(uint id,ref uint count,[Out] NativeTouchpadInjector.TouchInfo[] frame);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool GetPointerDeviceRects(nint device,out NativeTouchpadInjector.Rect deviceRect,out NativeTouchpadInjector.Rect screenRect);
    [DllImport("kernel32.dll",EntryPoint="GetProcAddress",ExactSpelling=true)] private static extern nint Ordinal(nint module,nint ordinal);
    private GetFrame? getFrame;
    private readonly HashSet<string> observedOrigins=[];
    private readonly TouchpadPointerFrames frames=new();
    internal void Prepare() {
        if(getFrame!=null)return;
        var dll=Native.GetModuleHandle("user32.dll");
        if(!NativeLibrary.TryGetExport(dll,"GetPointerFrameTouchpadInfo",out var entry))entry=Ordinal(dll,(nint)2693);
        if(entry==0)throw new NotSupportedException("Windows 未提供触控板触点帧接口。");
        getFrame=Marshal.GetDelegateForFunctionPointer<GetFrame>(entry);
    }
    internal void Reset()=>frames.Reset();
    internal TouchpadContact[]? Read(uint id,string origin) {
        if(getFrame==null)return null;
        if(observedOrigins.Add(origin))TouchpadDiagnostics.Write("worker","reading touchpad frame in "+origin);
        var buffer=new NativeTouchpadInjector.TouchInfo[16];uint count=(uint)buffer.Length;
        if(getFrame(id,ref count,buffer)==0)throw Failure("GetPointerFrameTouchpadInfo",origin);
        if(count==0 || count>buffer.Length)return null;
        if(!GetPointerDeviceRects(buffer[0].Pointer.Device,out var bounds,out _))throw Failure("GetPointerDeviceRects",origin);
        return frames.Read(buffer.Take((int)count).ToArray(),bounds);
    }
    private static System.ComponentModel.Win32Exception Failure(string api,string origin) {
        int code=Marshal.GetLastWin32Error();
        return new(code,$"{api} ({origin}, Win32 {code}): {new System.ComponentModel.Win32Exception(code).Message}");
    }
}
