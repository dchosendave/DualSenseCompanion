using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DualSenseCompanion.App;

namespace DualSenseCompanion.UI.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _vm;
    private readonly ControllerService _service;

    //Raised after the settings file was reloaded from disk
    public static event Action? SettingsReloaded;

    public SettingsPage(SettingsViewModel vm, ControllerService service)
    {
        InitializeComponent();
        _vm = vm;
        _service = service;
        DataContext = vm;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"DualSense Companion {version?.ToString(3) ?? ""}";
    }

    private void TestRumble_Click(object sender, RoutedEventArgs e) => _service.TestRumble();

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _vm.Settings.Save();

        try
        {
            Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true });
        }
        catch
        {
            Process.Start("notepad.exe", $"\"{Settings.FilePath}\"");
        }
    }

    private void ReloadSettings_Click(object sender, RoutedEventArgs e)
    {
        _service.ReplaceSettings(Settings.Load());
        _vm.RefreshAll();
        SettingsReloaded?.Invoke();
        Console.WriteLine("Settings reloaded.");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(Settings.Directory) { UseShellExecute = true });
    }
}
