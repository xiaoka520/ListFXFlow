using System.Runtime.InteropServices;
namespace ListFXFlow;

/// A gesture-only PT_TOUCHPAD device: Windows performs native gesture recognition
/// and user-configured global actions. It cannot synthesize pointer motion/clicks.
internal sealed class NativeTouchpadInjector : IDisposable {
    [StructLayout(LayoutKind.Sequential)] internal struct Creation {
        public uint Type,MaxCount,Feedback;
        public nint Monitor;
        public uint Width,Height,Options;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PointerInfo {
        public uint Type,Id,Frame,Flags;
        public nint Device,Target;
        public Native.Point Pixel,Himetric,PixelRaw,HimetricRaw;
        public uint Time,History;
        public int InputData;
        public uint Keys;
        public ulong Performance;
        public uint Change;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct TouchInfo {
        public PointerInfo Pointer;
        public uint Flags,Mask;
        public Rect Contact,RawContact;
        public uint Orientation,Pressure;
    }
    [StructLayout(LayoutKind.Explicit,Size=152)] internal struct PointerTypeInfo {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public TouchInfo Touch;
    }
    [DllImport("user32.dll",SetLastError=true)] private static extern nint CreateSyntheticPointerDevice2(ref Creation parameters);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool InjectSyntheticPointerInput(nint device,[In] PointerTypeInfo[] frames,uint count);
    [DllImport("user32.dll")] private static extern void DestroySyntheticPointerDevice(nint device);
    internal static bool Available { get; }=Probe();
    private static bool Probe() {
        if(!OperatingSystem.IsWindows() || !NativeLibrary.TryLoad("user32.dll",out var dll))return false;
        try { return NativeLibrary.TryGetExport(dll,"CreateSyntheticPointerDevice2",out _) && NativeLibrary.TryGetExport(dll,"InjectSyntheticPointerInput",out _); }
        finally { NativeLibrary.Free(dll); }
    }
    private readonly object gate=new();
    private readonly LinkedList<Message> queue=[];
    private readonly Thread worker;
    private readonly Action<string> failed;
    private readonly Action<Message,int>? applied;
    private nint device;
    private bool stopping,broken;
    private TouchpadContact[] contacts=[];
    private long busySince;
    internal long BusySince=>Interlocked.Read(ref busySince);
    internal NativeTouchpadInjector(Action<string> failed,Action<Message,int>? applied=null) {
        this.failed=failed;this.applied=applied;
        worker=new Thread(Run) { IsBackground=true,Name="ListFXFlow native touchpad" };worker.Start();
    }
    internal void Post(Message m) {
        lock(gate) {
            if(stopping || broken)return;
            if(m.Phase=="change" && queue.Last?.Value is { Phase:"change" } old && old.Contacts!.Select(c=>c.Id).SequenceEqual(m.Contacts!.Select(c=>c.Id))) { queue.Last.Value=m;return; }
            if(queue.Count>=128) { queue.Clear();queue.AddLast(new Message("touchpad") { Phase="cancel",Contacts=[] }); }
            else queue.AddLast(m);
            Monitor.Pulse(gate);
        }
    }
    internal void Reset() {
        lock(gate) { queue.Clear();queue.AddLast(new Message("touchpad") { Phase="cancel",Contacts=[] });Monitor.Pulse(gate); }
    }
    internal void Retry() {
        lock(gate) { if(!stopping) { queue.Clear();queue.AddLast(new Message("touchpadRetry"));Monitor.Pulse(gate); } }
    }
    private void Run() {
        try {
            while(true) {
                Message m;
                lock(gate) {
                    while(queue.Count==0 && !stopping)Monitor.Wait(gate);
                    if(queue.Count==0 && stopping)break;
                    m=queue.First!.Value;queue.RemoveFirst();
                }
                if(m.Type=="touchpadRetry") { broken=false;continue; }
                if(broken)continue;
                try { Interlocked.Exchange(ref busySince,Environment.TickCount64);Apply(m); }
                catch(Exception e) when(e is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException) {
                    broken=true;contacts=[];
                    if(device!=0) { DestroySyntheticPointerDevice(device);device=0; }
                    failed("原生手势暂不可用："+e.Message);
                }
                finally { Interlocked.Exchange(ref busySince,0); }
            }
        } finally { if(device!=0)DestroySyntheticPointerDevice(device); }
    }
    internal static PointerTypeInfo[] MakeFrame(TouchpadContact[] previous,TouchpadContact[] next,bool cancelled=false) {
        var ids=previous.Select(c=>c.Id).Union(next.Select(c=>c.Id));
        return ids.Select(id=> {
            var current=next.FirstOrDefault(c=>c.Id==id);
            var p=current ?? previous.First(c=>c.Id==id);
            var location=new Native.Point { X=(int)Math.Round(p.X*10000),Y=(int)Math.Round(p.Y*6000) };
            return new PointerTypeInfo { Type=5,Touch=new TouchInfo { Pointer=new PointerInfo {
                Type=5,Id=(uint)id,Flags=current!=null ? 0x4006u:cancelled ? 0xc000u:0x4000u,Himetric=location,HimetricRaw=location } } };
        }).ToArray();
    }
    private void Apply(Message m) {
        if(m.Phase=="begin") {
            Release();
            if(device==0) {
                if(!Available)throw new System.ComponentModel.Win32Exception("此 Windows 构建未提供原生触控板注入接口。");
                var parameters=new Creation { Type=5,MaxCount=5,Feedback=3,Width=10000,Height=6000,Options=3 };
                device=CreateSyntheticPointerDevice2(ref parameters);
                if(device==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        } else if(contacts.Length==0)return; // Orphan updates never start a stroke.
        var next=m.Phase is "end" or "cancel" ? []:m.Contacts!;
        // Up contacts count toward the API frame limit too. A complete identity
        // replacement must release the old frame before introducing five new IDs.
        if(contacts.Select(c=>c.Id).Union(next.Select(c=>c.Id)).Count()>5)Release();
        var frame=MakeFrame(contacts,next,m.Phase=="cancel");
        if(frame.Length>0 && !InjectSyntheticPointerInput(device,frame,(uint)frame.Length))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        contacts=next;
        applied?.Invoke(m,frame.Length>0 ? 1:0);
    }
    private void Release() {
        if(device==0 || contacts.Length==0)return;
        var up=MakeFrame(contacts,[],true);
        if(!InjectSyntheticPointerInput(device,up,(uint)up.Length))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        contacts=[];
    }
    public void Dispose() { Reset();lock(gate) { stopping=true;Monitor.Pulse(gate); }worker.Join(2000); }
}
