namespace ListFXFlow;

/// Manual escape overrides an automatic capture until that capture ends or the
/// foreground window changes. State is deliberately not persisted across sessions.
internal sealed class GameProtection {
    internal bool AutomaticEnabled=true;
    private bool manual,bypass;
    private nint capture;
    internal bool Locked=>manual || AutomaticEnabled && capture!=0 && !bypass;
    internal string Phase=>!Locked ? "off":manual ? "manual":"automatic";
    internal void Observe(nint window) { if(window!=capture)bypass=false;capture=window; }
    internal void SetManual(bool value) { manual=value;bypass=!value && capture!=0; }
    internal void Toggle()=>SetManual(!Locked);
    internal void Reset() { manual=false;bypass=false;capture=0; }
}

internal sealed class GameShortcut {
    private bool held,captured;
    internal (bool Consume,bool Toggle) Process(bool down,bool chord,bool available) {
        if(!down) { bool consume=captured;held=false;captured=false;return(consume,false); }
        if(held)return(captured,false);
        held=true;captured=chord && available;return(captured,captured);
    }
    internal void Reset() { held=false;captured=false; }
}

internal readonly record struct CaptureRect(int Left,int Top,int Right,int Bottom) {
    internal bool Valid=>Right>Left && Bottom>Top;
    internal bool Covers(CaptureRect other,int tolerance=2)=>Valid && other.Valid &&
        Left<=other.Left+tolerance && Top<=other.Top+tolerance && Right>=other.Right-tolerance && Bottom>=other.Bottom-tolerance;
}
internal static class GameCapturePolicy {
    internal static bool Captured(bool hidden,CaptureRect desktop,CaptureRect monitor,CaptureRect foreground,CaptureRect clip)=>
        hidden && desktop.Valid && monitor.Valid && foreground.Valid && clip.Valid &&
        (foreground.Covers(monitor) || !clip.Covers(desktop));
}
