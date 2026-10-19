namespace ListFXFlow;

public readonly record struct PointD(double X,double Y);
public readonly record struct ScreenRect(double X,double Y,double Width,double Height)
{
    public double MaxX => X+Width; public double MaxY => Y+Height;
    public bool Contains(PointD p) => p.X >= X && p.X < MaxX && p.Y >= Y && p.Y < MaxY;
    public PointD Clamp(PointD p) => new(Math.Clamp(p.X,X,MaxX-.01),Math.Clamp(p.Y,Y,MaxY-.01));
}
public sealed class Topology
{
    public string Side { get; set; } = "right";
    public double Offset { get; set; }
    public double MacAspect { get; set; } = 16.0/9;
    public double WindowsAspect { get; set; } = 16.0/10;
    public ScreenRect Mac => new(0,0,MacAspect*1000,1000);
    public ScreenRect Windows => Side switch {
        "left" => new(-WindowsAspect*1000,Offset,WindowsAspect*1000,1000),
        "top" => new(Offset,-1000,WindowsAspect*1000,1000),
        "bottom" => new(Offset,1000,WindowsAspect*1000,1000),
        _ => new(Mac.Width,Offset,WindowsAspect*1000,1000)
    };
    public ScreenRect Rect(string device) => device == "mac" ? Mac : Windows;
}
public sealed class PointerRouter
{
    public Topology Topology { get; } = new();
    public string Active { get; private set; } = "windows";
    public PointD Position { get; private set; } = new(500,500);
    public bool CrossingLocked {get;set;}
    public void UpdateTopology(Topology next) {
        var old=Topology.Rect(Active); double x=(Position.X-old.X)/old.Width,y=(Position.Y-old.Y)/old.Height;
        Topology.Side=next.Side; Topology.Offset=next.Offset; Topology.MacAspect=next.MacAspect; Topology.WindowsAspect=next.WindowsAspect;
        var rect=Topology.Rect(Active); Position=rect.Clamp(new(rect.X+x*rect.Width,rect.Y+y*rect.Height));
    }
    public bool Accept(Message message)
    {
        if(CrossingLocked && message.Active!=Active)return false;
        Active = message.Active!; var r = Topology.Rect(Active);
        Position = r.Clamp(new(r.X+message.X!.Value*r.Width,r.Y+message.Y!.Value*r.Height));
        return true;
    }
    public void Reset(string local) { CrossingLocked=false;Active=local; var r=Topology.Rect(local); Position=new(r.X+r.Width/2,r.Y+r.Height/2); }
    public Message Move(string local,double x,double y,double dx,double dy,double width,double height,bool mayCross=true)
    {
        var r=Topology.Rect(Active); PointD p;
        if (Active==local) {
            p=new(r.X+x/width*r.Width,r.Y+y/height*r.Height);
            if(x<=1 && dx<0) p=p with { X=r.X-1 };
            if(x>=width-2 && dx>0) p=p with { X=r.MaxX+1 };
            if(y<=1 && dy<0) p=p with { Y=r.Y-1 };
            if(y>=height-2 && dy>0) p=p with { Y=r.MaxY+1 };
        } else p=new(Position.X+dx*1000/height,Position.Y+dy*1000/height);
        string other=Active=="mac" ? "windows" : "mac";
        if(!CrossingLocked && mayCross && !r.Contains(p) && Crosses(Position,p,r,Topology.Rect(other))) { Active=other; Position=Topology.Rect(other).Clamp(p); }
        else Position=r.Clamp(p);
        var target=Topology.Rect(Active);
        return new("pointer") { Active=Active,X=(Position.X-target.X)/target.Width,Y=(Position.Y-target.Y)/target.Height };
    }
    private static bool Crosses(PointD a,PointD b,ScreenRect current,ScreenRect target)
    {
        if(target.Contains(b)) return true;
        double dx=b.X-a.X,dy=b.Y-a.Y; double? t=null;
        if(b.X<current.X && dx<0) t=(current.X-a.X)/dx;
        else if(b.X>=current.MaxX && dx>0) t=(current.MaxX-a.X)/dx;
        else if(b.Y<current.Y && dy<0) t=(current.Y-a.Y)/dy;
        else if(b.Y>=current.MaxY && dy>0) t=(current.MaxY-a.Y)/dy;
        return t is >=0 and <=1 && target.Contains(new(a.X+dx*t.Value+Math.Sign(dx)*.01,a.Y+dy*t.Value+Math.Sign(dy)*.01));
    }
}

internal static class CursorDeparture
{
    // Intersect the last local sample -> first remote sample with the local screen.
    // A fast remote mouse may skip the physical edge between transmitted samples.
    public static PointD? Project(ScreenRect screen,PointD from,PointD to,double width,double height) {
        if(!screen.Contains(from) || screen.Contains(to)) return null;
        double dx=to.X-from.X,dy=to.Y-from.Y,t=double.PositiveInfinity;
        if(dx<0) t=Math.Min(t,(screen.X-from.X)/dx);
        if(dx>0) t=Math.Min(t,(screen.MaxX-from.X)/dx);
        if(dy<0) t=Math.Min(t,(screen.Y-from.Y)/dy);
        if(dy>0) t=Math.Min(t,(screen.MaxY-from.Y)/dy);
        if(t<0 || t>1 || !double.IsFinite(t)) return null;
        return new(Math.Clamp((from.X+dx*t-screen.X)/screen.Width,0,1)*Math.Max(1,width-1),
                   Math.Clamp((from.Y+dy*t-screen.Y)/screen.Height,0,1)*Math.Max(1,height-1));
    }
}
