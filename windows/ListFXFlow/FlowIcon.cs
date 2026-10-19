using System.Windows;
using System.Windows.Media;

namespace ListFXFlow;

/// Embedded vector strokes: icons do not depend on installed fonts or emoji fallback.
public sealed class FlowIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(string),typeof(FlowIcon),new FrameworkPropertyMetadata("Pointer",FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty=DependencyProperty.Register(nameof(Stroke),typeof(Brush),typeof(FlowIcon),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get=>(string)GetValue(KindProperty); set=>SetValue(KindProperty,value); }
    public Brush Stroke { get=>(Brush)GetValue(StrokeProperty); set=>SetValue(StrokeProperty,value); }
    private static readonly Dictionary<string,Geometry> Icons=new Dictionary<string,string> {
        ["Pointer"]="M 5,3 L 6,20 L 10,15 L 14,21 L 17,19 L 13,13 L 20,12 Z",
        ["Workspace"]="M 3,4 L 12,4 L 12,11 L 3,11 Z M 12,13 L 21,13 L 21,20 L 12,20 Z M 16,4 L 20,4 L 20,8 M 4,16 L 4,20 L 8,20",
        ["Bolt"]="M 13,2 L 5,14 L 11,14 L 10,22 L 19,10 L 13,10 Z",
        ["Desktop"]="M 3,4 L 21,4 L 21,16 L 3,16 Z M 12,16 L 12,21 M 8,21 L 16,21",
        ["Laptop"]="M 5,4 L 19,4 L 19,16 L 5,16 Z M 3,16 L 21,16 L 23,20 L 1,20 Z",
        ["Keyboard"]="M 2,5 L 22,5 L 22,19 L 2,19 Z M 5,9 L 6,9 M 9,9 L 10,9 M 13,9 L 14,9 M 17,9 L 19,9 M 5,13 L 6,13 M 9,13 L 10,13 M 13,13 L 14,13 M 17,13 L 19,13 M 7,16 L 17,16",
        ["Left"]="M 20,12 L 4,12 M 10,6 L 4,12 L 10,18",
        ["Right"]="M 4,12 L 20,12 M 14,6 L 20,12 L 14,18",
        ["Up"]="M 12,20 L 12,4 M 6,10 L 12,4 L 18,10",
        ["Down"]="M 12,4 L 12,20 M 6,14 L 12,20 L 18,14",
        ["Refresh"]="M 20,10 A 8,8 0 1 0 20,16 M 20,4 L 20,10 L 14,10",
        ["Chevron"]="M 6,9 L 12,15 L 18,9"
    }.ToDictionary(p=>p.Key,p=> { var geometry=Geometry.Parse(p.Value); geometry.Freeze(); return geometry; });
    // The previous fixed 24×24 measure ignored explicit Width/Height and made icons
    // overlap or clip inside compact rows. Honour the declared size and the parent's
    // available size so every icon gets an exact layout slot.
    protected override Size MeasureOverride(Size availableSize)
    {
        double width=double.IsNaN(Width) ? 24 : Width;
        double height=double.IsNaN(Height) ? 24 : Height;
        if(!double.IsInfinity(availableSize.Width)) width=Math.Min(width,availableSize.Width);
        if(!double.IsInfinity(availableSize.Height)) height=Math.Min(height,availableSize.Height);
        return new Size(Math.Max(0,width),Math.Max(0,height));
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        if(ActualWidth<=0 || ActualHeight<=0 || !Icons.TryGetValue(Kind,out var geometry)) return;
        var pen=new Pen(Stroke,1.7) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
        var bounds=geometry.GetRenderBounds(pen);
        if(bounds.Width<=0 || bounds.Height<=0) return;
        // Keep a small, proportional gutter for round caps, and center the icon in its
        // slot instead of letting the largest dimension decide the origin.
        double gutter=Math.Min(2.5,Math.Min(ActualWidth,ActualHeight)*0.12);
        double scale=Math.Min(Math.Max(0,ActualWidth-gutter*2)/bounds.Width,Math.Max(0,ActualHeight-gutter*2)/bounds.Height);
        if(!double.IsFinite(scale) || scale<=0) return;
        double offsetX=(ActualWidth-bounds.Width*scale)/2-bounds.X*scale;
        double offsetY=(ActualHeight-bounds.Height*scale)/2-bounds.Y*scale;
        context.PushTransform(new MatrixTransform(scale,0,0,scale,offsetX,offsetY));
        context.DrawGeometry(null,pen,geometry);
        context.Pop();
    }
}
