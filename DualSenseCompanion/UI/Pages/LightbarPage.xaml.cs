using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace DualSenseCompanion.UI.Pages;

public partial class LightbarPage : UserControl
{
    private static readonly (string Name, Color Color)[] Presets =
    {
        ("Lightbar blue", Color.FromRgb(0x00, 0x50, 0xFF)),
        ("Cyan", Color.FromRgb(0x00, 0xD5, 0xFF)),
        ("Purple", Color.FromRgb(0x8B, 0x3D, 0xFF)),
        ("Pink", Color.FromRgb(0xFF, 0x2E, 0x9A)),
        ("Red", Color.FromRgb(0xFF, 0x10, 0x10)),
        ("Orange", Color.FromRgb(0xFF, 0x6A, 0x00)),
        ("Yellow", Color.FromRgb(0xFF, 0xD0, 0x00)),
        ("Green", Color.FromRgb(0x10, 0xE0, 0x40)),
        ("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    };

    private readonly SettingsViewModel _vm;

    public LightbarPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        foreach (var (name, color) in Presets)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Background = new SolidColorBrush(color),
                ToolTip = name,
            };

            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Lightbar color: {name}");

            swatch.Click += (s, e) => _vm.SetLightbarColor(color);
            Swatches.Children.Add(swatch);
        }
    }

    private void MoreColors_Click(object sender, RoutedEventArgs e)
    {
        var l = _vm.Settings.Lightbar;

        using var dialog = new WinForms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(l.Red, l.Green, l.Blue),
            FullOpen = true,
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _vm.SetLightbarColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        }
    }
}
