using System.Windows;
using System.Windows.Media;

namespace DualSenseCompanion.UI;

//Round stick visualizer: grey dot = physical stick, blue dot = what the game receives
public sealed class StickPlot : FrameworkElement
{
    private static readonly Brush Background = Frozen(Color.FromRgb(0xF6, 0xF7, 0xF9));
    private static readonly Brush DeadzoneFill = Frozen(Color.FromArgb(0x30, 0x6A, 0x6F, 0x7B));
    private static readonly Pen RingPen = FrozenPen(Color.FromRgb(0xE3, 0xE5, 0xEA), 1);
    private static readonly Pen OuterPen = FrozenPen(Color.FromArgb(0x99, 0x6C, 0x97, 0xFF), 1.2, dashed: true);
    private static readonly Pen CrossPen = FrozenPen(Color.FromRgb(0xEC, 0xEE, 0xF2), 1);
    private static readonly Brush RawDot = Frozen(Color.FromRgb(0x8B, 0x8F, 0x9A));
    private static readonly Brush OutDot = Frozen(Color.FromRgb(0x2F, 0x6B, 0xFF));

    public double Deadzone { get; set; }
    public double OuterDeadzone { get; set; }
    public double RawX { get; set; }
    public double RawY { get; set; }
    public double OutX { get; set; }
    public double OutY { get; set; }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double thickness, bool dashed = false)
    {
        var p = new Pen(Frozen(c), thickness);
        if (dashed)
        {
            p.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
        }
        p.Freeze();
        return p;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = double.IsInfinity(availableSize.Width) ? 180 : Math.Min(availableSize.Width, 180);
        return new Size(size, size);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double r = size / 2 - 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        dc.DrawEllipse(Background, RingPen, c, r, r);
        dc.DrawLine(CrossPen, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(CrossPen, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));

        if (Deadzone > 0)
        {
            dc.DrawEllipse(DeadzoneFill, null, c, r * Deadzone, r * Deadzone);
        }

        double outer = r * (1 - OuterDeadzone);
        dc.DrawEllipse(null, OuterPen, c, outer, outer);

        //Y is up-positive in both values
        dc.DrawEllipse(RawDot, null, new Point(c.X + RawX * r, c.Y - RawY * r), 5, 5);
        dc.DrawEllipse(OutDot, null, new Point(c.X + OutX * r, c.Y - OutY * r), 7, 7);
    }
}
