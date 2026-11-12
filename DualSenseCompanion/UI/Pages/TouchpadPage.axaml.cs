using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.UI.Pages;

public partial class TouchpadPage : UserControl, ILivePage
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1F));

    public TouchpadPage() : this(null!)
    {
    }

    public TouchpadPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public void Update(LiveSnapshot snapshot)
    {
        var input = snapshot.Input;

        bool any = Place(Finger1, input?.Touch1 ?? default) | Place(Finger2, input?.Touch2 ?? default);
        PadHint.IsVisible = !any;
        ClickRing.IsVisible = input?.IsPressed(DsButtons.Touchpad) == true;

        bool muted = snapshot.MicMuted;
        MicLed.Background = muted ? MutedBrush : this.Brush("TrackBrush");
        MicText.Text = muted ? "Microphone muted" : "Microphone on";
    }

    private bool Place(Ellipse dot, TouchPoint touch)
    {
        if (!touch.Active)
        {
            dot.IsVisible = false;
            return false;
        }

        dot.IsVisible = true;
        Canvas.SetLeft(dot, touch.X / (double)DualSenseParser.TouchpadWidth * Pad.Bounds.Width - dot.Width / 2);
        Canvas.SetTop(dot, touch.Y / (double)DualSenseParser.TouchpadHeight * Pad.Bounds.Height - dot.Height / 2);
        return true;
    }
}
