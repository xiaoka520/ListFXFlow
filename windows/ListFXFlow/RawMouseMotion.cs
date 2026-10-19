namespace ListFXFlow;

/// Relative hardware movement is independent of the parked desktop cursor.
/// Absolute devices need their own baseline so switching devices cannot cause a jump.
internal sealed class RawMouseMotion
{
    private readonly Dictionary<nint,PointD> absolutePositions=[];
    public PointD Read(nint device,ushort flags,int x,int y,double width,double height) {
        if((flags&1)==0) return new(x,y);
        var point=new PointD(x*Math.Max(1,width-1)/65535.0,y*Math.Max(1,height-1)/65535.0);
        bool known=absolutePositions.TryGetValue(device,out var previous);
        absolutePositions[device]=point;
        return known ? new(point.X-previous.X,point.Y-previous.Y) : new(0,0);
    }
    public void Clear() => absolutePositions.Clear();
    // Message/tick timestamps wrap every 49.7 days. Subtraction remains valid across wrap.
    public static int Since(uint messageTime,uint focusTime) => unchecked((int)(messageTime-focusTime));
}
