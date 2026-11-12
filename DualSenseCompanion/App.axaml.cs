using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using DualSenseCompanion.App;
using DualSenseCompanion.UI;

namespace DualSenseCompanion;

//Named CompanionApp to avoid clashing with the DualSenseCompanion.App namespace
public partial class CompanionApp : Application
{
    //Set by Program before the UI starts
    public static ControllerService Service { get; set; } = null!;
    public static bool StartMinimized { get; set; }

    //Screenshot and test tools load the theme only: no window, tray icon or update check
    public static bool ToolingMode { get; set; }

    public static new CompanionApp? Current => Application.Current as CompanionApp;

    //Raised after the light/dark theme changes, for parts that are colored in code
    public static event Action? ThemeChanged;

    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _toldAboutTray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ActualThemeVariantChanged += (s, e) => ThemeChanged?.Invoke();

        if (ToolingMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        ApplyTheme(Service.Settings.Theme);

        _window = new MainWindow(Service);
        _window.HiddenToTray += () =>
        {
            if (!_toldAboutTray)
            {
                _toldAboutTray = true;
                _tray?.ShowMessage("Still running in the tray so your controller keeps working. Right-click the icon to exit.");
            }
        };

        _tray = new TrayIcon(Service, action => Dispatcher.UIThread.Post(action));
        _tray.OpenRequested += ShowMainWindow;
        _tray.PageRequested += page =>
        {
            _window.Navigate(page);
            ShowMainWindow();
        };
        _tray.SettingsChanged += () => _window.ViewModel.RefreshAll();
        _tray.ExitRequested += ExitApp;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (s, e) => _tray?.Dispose();
        }

        if (StartMinimized)
        {
            _tray.ShowMessage("Running in the system tray. Click the icon to open.");
        }
        else
        {
            _window.Show();
        }

        if (Service.Settings.CheckForUpdates)
        {
            _ = Task.Run(async () =>
            {
                var update = await UpdateChecker.CheckAsync();

                if (update != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _window.ViewModel.SetUpdate(update, checkedNow: true);
                        _tray.ShowMessage($"Version {update.Version} is available. Open Settings to download it.");
                    });
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowMainWindow() => _window?.BringToFront();

    public void ExitApp()
    {
        if (_window != null)
        {
            _window.AllowClose = true;
            _window.Close();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    //System follows Windows' light/dark app setting, live
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current == null)
        {
            return;
        }

        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
