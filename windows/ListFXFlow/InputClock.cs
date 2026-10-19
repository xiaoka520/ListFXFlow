using System.Runtime.InteropServices;

namespace ListFXFlow;

/// A high-resolution waitable timer remains independent of hidden WPF windows.
/// It sleeps entirely when there is no scroll animation.
internal sealed class InputClock : IDisposable
{
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread thread;
    private volatile bool active,disposed;
    public InputClock(Action pulse) {
        thread=new Thread(()=> {
            IntPtr timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1f0003);
            if(timer==IntPtr.Zero) timer=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x1f0003);
            try {
                while(!disposed) {
                    if(!active) { wake.WaitOne(); continue; }
                    pulse();
                    // Source samples are coalesced at 250 Hz; received wheel
                    // deltas are injected on arrival without another timer.
                    long due=-40000;
                    if(timer!=IntPtr.Zero && SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false)) WaitForSingleObject(timer,100);
                    else wake.WaitOne(4);
                }
            } finally { if(timer!=IntPtr.Zero) CloseHandle(timer); }
        }) { IsBackground=true,Name="ListFXFlow input frame clock" }; thread.Start();
    }
    public void Start() { active=true; wake.Set(); }
    public void Stop() { active=false; }
    public void Dispose() { disposed=true; active=false; wake.Set(); thread.Join(1000); wake.Dispose(); }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr argument,bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
