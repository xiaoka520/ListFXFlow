namespace ListFXFlow;

/// Converts distance once, without creating a synthetic scroll animation. The
/// destination app decides whether/how to animate its native wheel messages.
public sealed class WheelTranslator
{
    private double pendingX,pendingY;
    private const int Quantum=1;
    // Fractional carry is retained but never keeps the frame clock spinning.
    public bool Pending=>Whole(pendingX,Quantum)!=0 || Whole(pendingY,Quantum)!=0;
    public void Add(double x,double y) {
        if(!double.IsFinite(x) || !double.IsFinite(y)) return;
        pendingX=Math.Clamp(pendingX+x*3,-60000,60000);
        pendingY=Math.Clamp(pendingY+y*3,-60000,60000);
    }
    public (int X,int Y) Take() {
        int x=Whole(pendingX,Quantum),y=Whole(pendingY,Quantum);
        pendingX-=x; pendingY-=y;
        return (x,y);
    }
    public void Reset() { pendingX=pendingY=0; }
    private static int Whole(double value,int quantum) {
        double count=value/quantum;
        double nearest=Math.Round(count);
        if(Math.Abs(count-nearest)<1e-9) count=nearest;
        return (int)Math.Truncate(count)*quantum;
    }
}
