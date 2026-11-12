using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;

namespace DualSenseCompanion.UI;

public partial class ControllerView : UserControl
{
    private const double StickTravel = 13;

    //Bluetooth delivers input in bursts; easing the drawing toward the latest value over a few milliseconds
    //hides that at high refresh rates. Only the drawing is smoothed, never what games receive.
    private const double SmoothingSeconds = 0.012;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastUpdate;
    private double _leftX, _leftY, _rightX, _rightY, _l2, _r2;

    private static readonly Brush KeyFill = Brushes.White;
    private static readonly Brush KeyStroke = Frozen(Color.FromRgb(0xC5, 0xC9, 0xD1));
    private static readonly Brush GlyphStroke = Frozen(Color.FromRgb(0x9B, 0xA0, 0xAA));
    private static readonly Brush CoreKey = Frozen(Color.FromRgb(0x2A, 0x2D, 0x35));
    private static readonly Brush StickStroke = Frozen(Color.FromRgb(0x3A, 0x3D, 0x46));
    private static readonly Brush LedOff = Frozen(Color.FromRgb(0x2C, 0x2F, 0x37));
    private static readonly Brush LedOn = Frozen(Color.FromRgb(0xF4, 0xF5, 0xF8));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x2F, 0x6B, 0xFF));
    private static readonly Brush MuteOrange = Frozen(Color.FromRgb(0xFF, 0x8A, 0x1F));

    private readonly DropShadowEffect _glow;

    public ControllerView()
    {
        InitializeComponent();
        _glow = (DropShadowEffect)FindResource("AccentGlow");
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Update(LiveSnapshot snapshot)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastUpdate, 0, 0.1);
        _lastUpdate = now;

        //Fraction of the remaining distance to cover this frame, independent of frame rate
        double follow = 1 - Math.Exp(-dt / SmoothingSeconds);

        var input = snapshot.Input;
        var buttons = input?.Buttons ?? DsButtons.None;
        bool Down(DsButtons b) => (buttons & b) != 0;

        //Face buttons and d-pad
        FaceButton(Triangle, TriangleGlyph, Down(DsButtons.Triangle));
        FaceButton(Circle, CircleGlyph, Down(DsButtons.Circle));
        FaceButton(Cross, CrossGlyph, Down(DsButtons.Cross));
        FaceButton(Square, SquareGlyph, Down(DsButtons.Square));

        WhiteKey(DpadUp, Down(DsButtons.DpadUp));
        WhiteKey(DpadDown, Down(DsButtons.DpadDown));
        WhiteKey(DpadLeft, Down(DsButtons.DpadLeft));
        WhiteKey(DpadRight, Down(DsButtons.DpadRight));
        WhiteKey(Create, Down(DsButtons.Create));
        WhiteKey(Options, Down(DsButtons.Options));

        CoreButton(L1, Down(DsButtons.L1));
        CoreButton(R1, Down(DsButtons.R1));
        CoreButton(PsButton, Down(DsButtons.PS));

        L2Fill.Opacity = Ease(ref _l2, (input?.L2 ?? 0) / 255.0, follow);
        R2Fill.Opacity = Ease(ref _r2, (input?.R2 ?? 0) / 255.0, follow);

        //Mute button: orange LED when the mic is muted, blue while pressed
        if (Down(DsButtons.Mute))
        {
            MuteButton.Fill = Accent;
            MuteButton.Effect = _glow;
        }
        else
        {
            bool muted = snapshot.Feedback.MicLed != MicLedMode.Off;
            MuteButton.Fill = muted ? MuteOrange : CoreKey;
            MuteButton.Effect = null;
        }

        //Sticks
        MoveStick(LeftStickMove, LeftStickCap, ref _leftX, ref _leftY, input?.LeftX ?? 128, input?.LeftY ?? 128, Down(DsButtons.L3), follow);
        MoveStick(RightStickMove, RightStickCap, ref _rightX, ref _rightY, input?.RightX ?? 128, input?.RightY ?? 128, Down(DsButtons.R3), follow);

        //Touchpad
        PlaceTouch(Touch1, input?.Touch1 ?? default);
        PlaceTouch(Touch2, input?.Touch2 ?? default);
        TouchpadClick.Visibility = Down(DsButtons.Touchpad) ? Visibility.Visible : Visibility.Collapsed;

        //Lightbar shows exactly what is sent to the controller
        var f = snapshot.Feedback;
        var color = Color.FromRgb(f.LightbarRed, f.LightbarGreen, f.LightbarBlue);
        SetLightbar(LightbarLeft, color);
        SetLightbar(LightbarRight, color);

        //Player LEDs (bit 0 = leftmost)
        for (int i = 0; i < PlayerLeds.Children.Count; i++)
        {
            ((Rectangle)PlayerLeds.Children[i]).Fill = (f.PlayerLeds & (1 << i)) != 0 ? LedOn : LedOff;
        }
    }

    private void FaceButton(Shape button, Shape glyph, bool pressed)
    {
        button.Fill = pressed ? Accent : KeyFill;
        button.Stroke = pressed ? Accent : KeyStroke;
        button.Effect = pressed ? _glow : null;
        glyph.Stroke = pressed ? Brushes.White : GlyphStroke;
    }

    private void WhiteKey(Shape key, bool pressed)
    {
        key.Fill = pressed ? Accent : KeyFill;
        key.Stroke = pressed ? Accent : KeyStroke;
        key.Effect = pressed ? _glow : null;
    }

    private void CoreButton(Shape key, bool pressed)
    {
        key.Fill = pressed ? Accent : CoreKey;
        key.Effect = pressed ? _glow : null;
    }

    private static double Ease(ref double current, double target, double follow)
    {
        current += (target - current) * follow;

        //Snap when close so the drawing settles exactly
        if (Math.Abs(target - current) < 0.002)
        {
            current = target;
        }

        return current;
    }

    private void MoveStick(TranslateTransform move, Shape cap, ref double shownX, ref double shownY, byte x, byte y, bool pressed, double follow)
    {
        move.X = Ease(ref shownX, InputMapper.NormalizeAxis(x), follow) * StickTravel;
        move.Y = Ease(ref shownY, InputMapper.NormalizeAxis(y), follow) * StickTravel;
        cap.Stroke = pressed ? Accent : StickStroke;
        cap.Effect = pressed ? _glow : null;
    }

    private void PlaceTouch(Ellipse dot, TouchPoint touch)
    {
        if (!touch.Active)
        {
            dot.Visibility = Visibility.Collapsed;
            return;
        }

        dot.Visibility = Visibility.Visible;
        Canvas.SetLeft(dot, touch.X / (double)DualSenseParser.TouchpadWidth * Touchpad.Width - dot.Width / 2);
        Canvas.SetTop(dot, touch.Y / (double)DualSenseParser.TouchpadHeight * Touchpad.Height - dot.Height / 2);
    }

    private static void SetLightbar(Path strip, Color color)
    {
        //An "off" lightbar is drawn dark instead of invisible
        var visible = color.R + color.G + color.B < 20 ? Color.FromRgb(0x3A, 0x3D, 0x46) : color;

        if (strip.Stroke is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = visible;
        }
        else
        {
            strip.Stroke = new SolidColorBrush(visible);
        }

        if (strip.Effect is DropShadowEffect glow)
        {
            glow.Color = visible;
            glow.Opacity = color.R + color.G + color.B < 20 ? 0 : 1;
        }
    }
}
