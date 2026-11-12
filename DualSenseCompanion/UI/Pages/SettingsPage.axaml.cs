using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DualSenseCompanion.App;

namespace DualSenseCompanion.UI.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _vm;
    private readonly ControllerService _service;

    //Raised after the settings file was reloaded from disk
    public static event Action? SettingsReloaded;

    public SettingsPage() : this(null!, null!)
    {
    }

    public SettingsPage(SettingsViewModel vm, ControllerService service)
    {
        InitializeComponent();
        _vm = vm;
        _service = service;
        DataContext = vm;

        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;
        VersionText.Text = $"DualSense Companion {version?.ToString(3) ?? ""}";
    }

    private void TestRumble_Click(object? sender, RoutedEventArgs e) => _service.TestRumble();

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;

        try
        {
            await _vm.CheckForUpdatesNowAsync();
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void DownloadUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.UpdateUrl != null)
        {
            Process.Start(new ProcessStartInfo(_vm.UpdateUrl) { UseShellExecute = true });
        }
    }

    private sealed record ResultRow(string Icon, IBrush IconBrush, string Name, string Detail)
    {
        //What a screen reader says instead of the icon glyph
        public string Spoken => IconBrush == Pass ? "Passed" : IconBrush == Fail ? "Failed" : "Skipped";
    }

    private async void SelfTest_Click(object? sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        SelfTestSummary.Text = "Running...";
        SelfTestResults.ItemsSource = null;

        try
        {
            var report = await _service.RunSelfTestAsync();

            SelfTestResults.ItemsSource = report.Checks.Select(c => c.Result switch
            {
                CheckResult.Pass => new ResultRow("\uE73E", Pass, c.Name, c.Detail),
                CheckResult.Fail => new ResultRow("\uE711", Fail, c.Name, c.Detail),
                _ => new ResultRow("\uE738", Skipped, c.Name, c.Detail),
            }).ToList();

            SelfTestSummary.Text = report.AllPassed ? "Everything works." : "Some checks failed.";
        }
        catch (Exception ex)
        {
            SelfTestSummary.Text = $"Self-test failed: {ex.Message}";
        }
        finally
        {
            SelfTestButton.IsEnabled = true;
        }
    }

    private static readonly IBrush Pass = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly IBrush Fail = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly IBrush Skipped = new SolidColorBrush(Color.FromRgb(0x9B, 0xA0, 0xAA));

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
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

    private void ReloadSettings_Click(object? sender, RoutedEventArgs e)
    {
        _service.ReplaceSettings(Settings.Load());
        _vm.RefreshAll();
        SettingsReloaded?.Invoke();
        Console.WriteLine("Settings reloaded.");
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(Settings.Directory) { UseShellExecute = true });
    }
}
