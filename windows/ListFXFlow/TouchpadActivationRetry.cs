namespace ListFXFlow;

/// A denied focus request is an ownership transition, not a broken SDK worker.
/// Requests are bounded per crossing and replies from an old crossing expire.
internal sealed class TouchpadActivationRetry {
    private long epoch,next;
    private int attempts;
    internal bool Pending { get;private set; }
    internal void Begin(long epoch,long now) { this.epoch=epoch;next=now;attempts=0;Pending=true; }
    internal void Stop()=>Pending=false;
    internal bool Accepts(long epoch)=>Pending && this.epoch==epoch;
    internal bool Request(long now) {
        if(!Pending || attempts>=4 || now<next)return false;
        attempts++;next=now+250;return true;
    }
    internal bool Expired(long now)=>Pending && attempts>=4 && now>=next;
}
