using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Features;
using DualSenseCompanion.UI.Pages;

namespace DualSenseCompanion.UI;

//Pages that show live controller data
public interface ILivePage
{
    void Update(LiveSnapshot snapshot);
}

public enum PageId
{
    Home,
    Test,
    Lightbar,
    Sticks,
    Triggers,
    Gyro,
    Touchpad,
    Buttons,
    Settings,
    Log,
}

public partial class MainWindow : Window
{
    private static readonly Brush ConnectedDot = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush LowBattery = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    private readonly ControllerService _service;
    private bool _liveRunning;
    private TimeSpan _lastFrame;
    private ControllerStatus? _shownStatus;
    private readonly Dictionary<PageId, Func<FrameworkElement>> _factories;
    private readonly Dictionary<PageId, FrameworkElement> _pages = new Dictionary<PageId, FrameworkElement>();
    private readonly Dictionary<PageId, RadioButton> _navButtons;

    public SettingsViewModel ViewModel { get; }

    //Set by the app when it really exits, so closing doesn't just hide the window
    public bool AllowClose { get; set; }
    public event Action? HiddenToTray;

    public MainWindow(ControllerService service)
    {
        InitializeComponent();

        _service = service;
        ViewModel = new SettingsViewModel(service);
        DataContext = ViewModel;

        _factories = new Dictionary<PageId, Func<FrameworkElement>>
        {
            [PageId.Home] = () => new HomePage(ViewModel),
            [PageId.Test] = () => new TestPage(),
            [PageId.Lightbar] = () => new LightbarPage(ViewModel),
            [PageId.Sticks] = () => new SticksPage(ViewModel),
            [PageId.Triggers] = () => new TriggersPage(ViewModel),
            [PageId.Gyro] = () => new GyroPage(ViewModel, _service),
            [PageId.Touchpad] = () => new TouchpadPage(ViewModel),
            [PageId.Buttons] = () => new ButtonsPage(ViewModel),
            [PageId.Settings] = () => new SettingsPage(ViewModel, _service),
            [PageId.Log] = () => new LogPage(),
        };

        _navButtons = new Dictionary<PageId, RadioButton>
        {
            [PageId.Home] = NavHome,
            [PageId.Test] = NavTest,
            [PageId.Lightbar] = NavLightbar,
            [PageId.Sticks] = NavSticks,
            [PageId.Triggers] = NavTriggers,
            [PageId.Gyro] = NavGyro,
            [PageId.Touchpad] = NavTouchpad,
            [PageId.Buttons] = NavButtons,
            [PageId.Settings] = NavSettings,
            [PageId.Log] = NavLog,
        };

        foreach (var (id, button) in _navButtons)
        {
            button.Checked += (s, e) => ShowPage(id);
        }

        //Live view refreshes once per displayed frame, so it follows the monitor's refresh rate (60, 144, 180 Hz...)
        IsVisibleChanged += (s, e) => SetLiveUpdates(IsVisible && WindowState != WindowState.Minimized);
        StateChanged += (s, e) => SetLiveUpdates(IsVisible && WindowState != WindowState.Minimized);

        StateChanged += (s, e) =>
        {
            //A borderless maximized window overhangs the screen; pad it back in
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        //Redraw code-colored parts after a theme switch
        ThemeManager.Changed += () =>
        {
            _shownStatus = null;
            UpdateLive();
        };

        NavHome.IsChecked = true;
    }

    public void Navigate(PageId page)
    {
        _navButtons[page].IsChecked = true;
    }

    public void BringToFront()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void OnSettingsReplaced() => ViewModel.RefreshAll();

    private void ShowPage(PageId id)
    {
        if (!_pages.TryGetValue(id, out var page))
        {
            page = _factories[id]();
            _pages[id] = page;
        }

        PageHost.Content = page;
        Scroller.ScrollToTop();
        UpdateLive();
    }

    private void SetLiveUpdates(bool on)
    {
        if (on == _liveRunning)
        {
            return;
        }

        _liveRunning = on;

        if (on)
        {
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        //Rendering can fire more than once per frame; update once per new frame only
        var time = ((RenderingEventArgs)e).RenderingTime;

        if (time == _lastFrame)
        {
            return;
        }

        _lastFrame = time;
        UpdateLive();
    }

    private void UpdateLive()
    {
        var snapshot = _service.GetSnapshot();

        if (snapshot.Status != _shownStatus)
        {
            _shownStatus = snapshot.Status;
            UpdateStatus(snapshot.Status);
        }

        if (PageHost.Content is ILivePage live)
        {
            live.Update(snapshot);
        }
    }

    private void UpdateStatus(ControllerStatus status)
    {
        if (!status.Connected)
        {
            StatusDot.Fill = (Brush)FindResource("StatusIdleBrush");
            StatusTitle.Text = "Not connected";
            StatusDetail.Text = "Plug in or pair your controller";
            StatusBatteryRow.Visibility = Visibility.Collapsed;
            return;
        }

        StatusDot.Fill = ConnectedDot;
        StatusTitle.Text = $"Connected · {status.Connection}";

        if (!status.HasBattery)
        {
            StatusDetail.Text = $"Emulating {status.Emulation}";
            StatusBatteryRow.Visibility = Visibility.Collapsed;
            return;
        }

        string charge = status.Battery switch
        {
            BatteryState.Charging => " · charging",
            BatteryState.Full => " · full",
            _ => "",
        };

        StatusDetail.Text = status.BatteryRemaining is { } left
            ? $"Battery {status.BatteryPercent}%, {BatteryEstimator.Describe(left).Replace("about ", "~")}"
            : $"Battery {status.BatteryPercent}%{charge}";
        StatusBatteryRow.Visibility = Visibility.Visible;
        StatusBatteryFill.Width = Math.Max(0, (StatusBatteryRow.ActualWidth > 0 ? StatusBatteryRow.ActualWidth : 180) * status.BatteryPercent / 100.0);
        StatusBatteryFill.Background = status.IsLowBattery(_service.Settings.Battery.LowThreshold)
            ? LowBattery
            : (Brush)FindResource("AccentBrush");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    //Closing hides to the tray; the app keeps running for your games
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }

        base.OnClosing(e);
    }
}
