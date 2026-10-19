namespace ListFXFlow;

/// WinRT reports one changed pointer at a time. Publish one coherent snapshot
/// per frame instead of exposing half-updated pinches to the receiver.
internal sealed class TouchpadContactBatch {
    internal readonly record struct Sample(uint Id,uint Frame,bool Live,bool Cancelled,double X,double Y);
    private readonly Dictionary<uint,TouchpadContact> contacts=[];
    private uint? frame;
    private uint? cancelledFrame;
    private bool dirty;
    internal void Reset() { contacts.Clear();frame=null;cancelledFrame=null;dirty=false; }
    internal TouchpadContact[]? Push(Sample sample) {
        var previous=frame is {} previousFrame && previousFrame!=sample.Frame ? Flush():null;
        if(frame!=sample.Frame)cancelledFrame=null;
        frame=sample.Frame;
        if(sample.Cancelled) { contacts.Clear();cancelledFrame=sample.Frame;dirty=true;return previous; }
        if(cancelledFrame==sample.Frame)return previous;
        if(!sample.Live)contacts.Remove(sample.Id);
        else {
            if(!double.IsFinite(sample.X) || !double.IsFinite(sample.Y))throw new ArgumentException("无效的触控板物理坐标。");
            int slot=contacts.TryGetValue(sample.Id,out var oldContact) ? oldContact.Id:Enumerable.Range(0,5).FirstOrDefault(i=>!contacts.Values.Any(p=>p.Id==i),-1);
            if(slot<0)throw new ArgumentException("触点数量超过 5。");
            contacts[sample.Id]=new(slot,Math.Clamp(sample.X,0,1),Math.Clamp(sample.Y,0,1));
        }
        dirty=true;return previous;
    }
    internal TouchpadContact[]? Flush() {
        if(!dirty)return null;dirty=false;
        return contacts.Values.OrderBy(p=>p.Id).ToArray();
    }
}
