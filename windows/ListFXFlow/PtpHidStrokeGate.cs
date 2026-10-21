namespace ListFXFlow;

/// Raw observation is not ownership. Only a new SDK-routed global gesture may
/// start remote output; an already-running Windows gesture stays local.
internal sealed class PtpHidStrokeGate {
    private bool owned,started;
    internal void Pressed()=>owned=true;
    internal void Reset()=>owned=started=false;
    internal bool Accept(int contacts) {
        if(contacts==0) {Reset();return true;}
        if(!owned)return false;
        if(contacts>=3) {started=true;return true;}
        if(!started)return false;
        Reset();return true; // Allow exactly the final partial-lift/end frame.
    }
}
