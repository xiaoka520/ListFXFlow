using System.Runtime.InteropServices;

namespace ListFXFlow;

/// Read-only, process-local observation. Never changes ClipCursor, visibility,
/// foreground focus, game hooks, or raw-input registration.
internal static class GameCaptureProbe {
    internal static nint Read(int workerPid) {
        nint window=Native.GetForegroundWindow();
        if(window==0)return 0;
        Native.GetWindowThreadProcessId(window,out uint pid);
        if(pid==0 || pid==Environment.ProcessId || pid==workerPid)return 0;
        var cursor=new Native.CursorInfo { Size=(uint)Marshal.SizeOf<Native.CursorInfo>() };
        if(!Native.GetCursorInfo(ref cursor) || (cursor.Flags&2)!=0 || (cursor.Flags&1)!=0 && cursor.Cursor!=0 ||
           !Native.GetWindowRect(window,out var foreground) || !Native.GetClipCursor(out var clip))return 0;
        var monitor=new Native.MonitorInfo {Size=(uint)Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromWindow(window,2),ref monitor))return 0;
        int x=Native.GetSystemMetrics(76),y=Native.GetSystemMetrics(77);
        var desktop=new CaptureRect(x,y,x+Native.GetSystemMetrics(78),y+Native.GetSystemMetrics(79));
        return GameCapturePolicy.Captured(true,desktop,monitor.Monitor.Value,foreground.Value,clip.Value) ? window:0;
    }
}
