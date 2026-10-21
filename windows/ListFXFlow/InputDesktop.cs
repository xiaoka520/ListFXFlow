using System.Runtime.InteropServices;
using System.Text;

namespace ListFXFlow;

internal static class InputDesktop
{
    // Read desktop identity only. Never switch desktops or relax the UAC policy.
    internal static bool Available() {
        nint input=OpenInputDesktop(0,false,1); // DESKTOP_READOBJECTS
        if(input==0)return false;
        try {
            string? current=Name(GetThreadDesktop(GetCurrentThreadId()));
            return current!=null && current==Name(input);
        } finally { CloseDesktop(input); }
    }
    private static string? Name(nint desktop) {
        var name=new StringBuilder(256);
        return desktop!=0 && GetUserObjectInformation(desktop,2,name,name.Capacity*2,out _) ? name.ToString():null;
    }
    [DllImport("user32.dll",SetLastError=true)] private static extern nint OpenInputDesktop(uint flags,[MarshalAs(UnmanagedType.Bool)] bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",EntryPoint="GetUserObjectInformationW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool GetUserObjectInformation(nint handle,int index,StringBuilder value,int length,out int needed);
}

/// Epochs prevent buffered input from crossing a secure-desktop transition.
/// Only successfully injected downs need a release when the normal desktop returns.
internal sealed class DesktopInputGate
{
    private readonly object sync=new();
    private long epoch;
    private bool paused;
    private readonly Dictionary<(ushort Scan,ushort Vk,uint Extended),Native.Input> keys=[];
    private readonly Dictionary<uint,Native.Input> buttons=[];
    internal bool Paused { get { lock(sync)return paused; } }
    internal long Epoch { get { lock(sync)return epoch; } }
    internal bool Allows(long value) { lock(sync)return !paused && epoch==value; }
    internal bool Pause() { lock(sync) { if(paused)return false;paused=true;epoch++;return true; } }
    internal bool Resume() { lock(sync) { if(!paused)return false;paused=false;epoch++;return true; } }
    internal Native.Input[] Releases { get { lock(sync)return keys.Values.Concat(buttons.Values).ToArray(); } }
    internal void Delivered(Native.Input input) {
        lock(sync) {
            if(input.Type==1) {
                var key=input.Union.Key;
                var id=(key.Scan,key.Vk,key.Flags&1u);
                if((key.Flags&2)!=0)keys.Remove(id);
                else { input.Union.Key.Flags|=2;keys[id]=input; }
            } else if(input.Type==0) {
                uint flags=input.Union.Mouse.Flags;
                foreach(var (down,up) in new (uint,uint)[] {(2,4),(8,16),(32,64),(128,256)}) {
                    uint id=down==128 ? down+input.Union.Mouse.Data:down;
                    if((flags&up)!=0)buttons.Remove(id);
                    else if((flags&down)!=0) {
                        var release=input;release.Union.Mouse.Flags=up;
                        buttons[id]=release;
                    }
                }
            }
        }
    }
}
