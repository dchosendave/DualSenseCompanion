using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;
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
    private static readonly Brush DisconnectedDot = new SolidColorBrush(Color.FromRgb(0x5B, 0x5F, 0x69));
    private static readonly Brush LowBattery = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    private readonly ControllerService _service;
    private readonly DispatcherTimer _liveTimer;
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

        _liveTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _liveTimer.Tick += (s, e) => UpdateLive();

        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                _liveTimer.Start();
            }
            else
            {
                _liveTimer.Stop();
            }
        };

        StateChanged += (s, e) =>
        {
            //A borderless maximized window overhangs the screen; pad it back in
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
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

    private void UpdateLive()
    {
        var snapshot = _service.GetSnapshot();
        UpdateStatus(snapshot.Status);

        if (PageHost.Content is ILivePage live)
        {
            live.Update(snapshot);
        }
    }

    private void UpdateStatus(ControllerStatus status)
    {
        if (!status.Connected)
        {
            StatusDot.Fill = DisconnectedDot;
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

        StatusDetail.Text = $"Battery {status.BatteryPercent}%{charge}";
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
