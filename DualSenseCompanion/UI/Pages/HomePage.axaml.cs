using Avalonia.Controls;
using Avalonia.Media;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Features;

namespace DualSenseCompanion.UI.Pages;

public partial class HomePage : UserControl, ILivePage
{
    private static readonly IBrush LowBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private readonly SettingsViewModel _vm;

    public HomePage() : this(null!)
    {
    }

    public HomePage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public void Update(LiveSnapshot snapshot)
    {
        Controller.Update(snapshot);

        if (!ReferenceEquals(Warnings.ItemsSource, snapshot.Conflicts))
        {
            Warnings.ItemsSource = snapshot.Conflicts;
        }

        var status = snapshot.Status;
        ConnectHint.IsVisible = !status.Connected;
        Subtitle.Text = status.Connected
            ? "Press any button, move the sticks or touch the touchpad to see it live."
            : "Waiting for your DualSense. Everything below can be set up already.";

        if (!status.Connected)
        {
            ConnectionText.Text = "Not connected";
            ConnectionDetail.Text = "Waiting for a DualSense";
            BatteryText.Text = "—";
            BatteryDetail.Text = "Shown once connected";
            BatteryFill.Width = 0;
            return;
        }

        ConnectionText.Text = status.Connection;
        ConnectionDetail.Text = $"Emulating {status.Emulation}";

        if (!status.HasBattery)
        {
            BatteryText.Text = "—";
            BatteryDetail.Text = "Reading battery...";
            BatteryFill.Width = 0;
            return;
        }

        BatteryText.Text = $"{status.BatteryPercent}%";
        BatteryDetail.Text = status.Battery switch
        {
            BatteryState.Charging => "Charging",
            BatteryState.Full => "Fully charged",
            BatteryState.Error => "Battery error",
            _ => (status.IsLowBattery(_vm.Settings.Battery.LowThreshold) ? "Low, charge soon" : "On battery") +
                 (status.BatteryRemaining is { } left ? $", {BatteryEstimator.Describe(left)}" : ", estimating time left"),
        };

        BatteryFill.Width = BatteryBar.Bounds.Width * status.BatteryPercent / 100.0;
        BatteryFill.Background = status.IsLowBattery(_vm.Settings.Battery.LowThreshold)
            ? LowBrush
            : this.Brush("AccentBrush");
    }
}
