using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.UI.Pages;

public partial class TouchpadPage : UserControl, ILivePage
{
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1F));

    public TouchpadPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public void Update(LiveSnapshot snapshot)
    {
        var input = snapshot.Input;

        bool any = Place(Finger1, input?.Touch1 ?? default) | Place(Finger2, input?.Touch2 ?? default);
        PadHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ClickRing.Visibility = input?.IsPressed(DsButtons.Touchpad) == true ? Visibility.Visible : Visibility.Collapsed;

        bool muted = snapshot.MicMuted;
        MicLed.Background = muted ? MutedBrush : (Brush)FindResource("TrackBrush");
        MicText.Text = muted ? "Microphone muted" : "Microphone on";
    }

    private bool Place(Ellipse dot, TouchPoint touch)
    {
        if (!touch.Active)
        {
            dot.Visibility = Visibility.Collapsed;
            return false;
        }

        dot.Visibility = Visibility.Visible;
        Canvas.SetLeft(dot, touch.X / (double)DualSenseParser.TouchpadWidth * Pad.ActualWidth - dot.Width / 2);
        Canvas.SetTop(dot, touch.Y / (double)DualSenseParser.TouchpadHeight * Pad.ActualHeight - dot.Height / 2);
        return true;
    }
}
