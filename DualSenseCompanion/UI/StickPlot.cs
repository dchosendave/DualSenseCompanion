using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DualSenseCompanion.UI;

//Round stick visualizer: grey dot = physical stick, blue dot = what the game receives
public sealed class StickPlot : Control
{
    private static readonly IBrush DeadzoneFill = new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0x8E, 0x8E, 0x93));
    private static readonly IPen OuterPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0x6C, 0x97, 0xFF)), 1.2, new ImmutableDashStyle(new double[] { 3, 3 }, 0));

    public double Deadzone { get; set; }
    public double OuterDeadzone { get; set; }
    public double RawX { get; set; }
    public double RawY { get; set; }
    public double OutX { get; set; }
    public double OutY { get; set; }

    //Theme colors, looked up each frame so a light/dark switch applies immediately
    private IBrush ThemeBrush(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : new ImmutableSolidColorBrush(fallback);

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = double.IsInfinity(availableSize.Width) ? 130 : Math.Min(availableSize.Width, 130);
        return new Size(size, size);
    }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        double r = size / 2 - 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);

        var ring = new Pen(ThemeBrush("LineBrush", Color.FromRgb(0xE5, 0xE5, 0xEA)), 1);
        var cross = new Pen(ThemeBrush("HoverBrush", Color.FromRgb(0xEC, 0xEE, 0xF2)), 1);

        dc.DrawEllipse(ThemeBrush("SurfaceBrush", Color.FromRgb(0xF3, 0xF4, 0xF6)), ring, c, r, r);
        dc.DrawLine(cross, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(cross, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));

        if (Deadzone > 0)
        {
            dc.DrawEllipse(DeadzoneFill, null, c, r * Deadzone, r * Deadzone);
        }

        double outer = r * (1 - OuterDeadzone);
        dc.DrawEllipse(null, OuterPen, c, outer, outer);

        //Y is up-positive in both values
        dc.DrawEllipse(ThemeBrush("TextSecondaryBrush", Color.FromRgb(0x8B, 0x8F, 0x9A)), null, new Point(c.X + RawX * r, c.Y - RawY * r), 5, 5);
        dc.DrawEllipse(ThemeBrush("AccentBrush", Color.FromRgb(0x2F, 0x6B, 0xFF)), null, new Point(c.X + OutX * r, c.Y - OutY * r), 7, 7);
    }
}
