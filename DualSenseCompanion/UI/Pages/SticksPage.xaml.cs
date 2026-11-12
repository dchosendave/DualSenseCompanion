using System.Windows.Controls;
using DualSenseCompanion.App;
using DualSenseCompanion.Emulation;

namespace DualSenseCompanion.UI.Pages;

public partial class SticksPage : UserControl, ILivePage
{
    private readonly SettingsViewModel _vm;

    public SticksPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public void Update(LiveSnapshot snapshot)
    {
        var input = snapshot.Input;
        var settings = _vm.Settings;

        UpdatePlot(LeftPlot, input?.LeftX ?? 128, input?.LeftY ?? 128, settings.LeftStick);
        UpdatePlot(RightPlot, input?.RightX ?? 128, input?.RightY ?? 128, settings.RightStick);

        L2Fill.Width = L2Bar.ActualWidth * (input?.L2 ?? 0) / 255.0;
        R2Fill.Width = R2Bar.ActualWidth * (input?.R2 ?? 0) / 255.0;
    }

    //The output is recomputed here so the plot follows slider changes even before the next report
    private static void UpdatePlot(StickPlot plot, byte x, byte y, StickSettings s)
    {
        plot.RawX = InputMapper.NormalizeAxis(x);
        plot.RawY = -InputMapper.NormalizeAxis(y);
        (double outX, double outY) = InputMapper.ApplyStick(x, y, s);
        plot.OutX = outX;
        plot.OutY = outY;
        plot.Deadzone = s.Deadzone;
        plot.OuterDeadzone = s.OuterDeadzone;
        plot.InvalidateVisual();
    }
}
