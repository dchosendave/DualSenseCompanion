using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DualSenseCompanion.App;

namespace DualSenseCompanion.UI.Pages;

public partial class GyroPage : UserControl, ILivePage
{
    private const double FullScaleDegreesPerSecond = 360;

    private readonly ControllerService _service;

    public GyroPage() : this(null!, null!)
    {
    }

    public GyroPage(SettingsViewModel vm, ControllerService service)
    {
        InitializeComponent();
        DataContext = vm;
        _service = service;
    }

    public void Update(LiveSnapshot snapshot)
    {
        bool hasMotion = snapshot.Input?.HasExtendedData == true;

        SetBar(PitchTrack, PitchFill, PitchText, hasMotion ? snapshot.GyroPitch : 0);
        SetBar(YawTrack, YawFill, YawText, hasMotion ? snapshot.GyroYaw : 0);
        SetBar(RollTrack, RollFill, RollText, hasMotion ? snapshot.GyroRoll : 0);
    }

    //Bar grows left or right from the center line
    private static void SetBar(Control track, Control fill, TextBlock text, float rate)
    {
        double half = track.Bounds.Width / 2;
        double length = Math.Min(Math.Abs(rate) / FullScaleDegreesPerSecond, 1) * half;

        fill.Width = length;
        fill.Margin = new Thickness(rate >= 0 ? half : half - length, 0, 0, 0);
        text.Text = $"{rate,6:0}°/s";
    }

    private void Calibrate_Click(object? sender, RoutedEventArgs e)
    {
        _service.RecalibrateGyro();
        CalibrateHint.Text = "Calibrating... keep it still.";

        DispatcherTimer.RunOnce(
            () => CalibrateHint.Text = "Done. Put the controller on a table and don't touch it for a second to calibrate again.",
            TimeSpan.FromSeconds(2));
    }
}
