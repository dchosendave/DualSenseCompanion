using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    private static readonly IBrush ConnectedDot = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly IBrush LowBattery = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    private readonly ControllerService _service;
    private readonly Dictionary<PageId, Func<Control>> _factories;
    private readonly Dictionary<PageId, Control> _pages = new();
    private readonly Dictionary<PageId, RadioButton> _navButtons;
    private ControllerStatus? _shownStatus;
    private bool _liveRunning;

    public SettingsViewModel ViewModel { get; }

    //Screenshot tooling can supply sample data instead of the live controller
    public Func<LiveSnapshot>? SnapshotSource { get; set; }

    //Set when the app really exits, so closing doesn't just hide the window
    public bool AllowClose { get; set; }
    public event Action? HiddenToTray;

    //Needed by the XAML loader for previews; the app always uses the other constructor
    public MainWindow() : this(CompanionApp.Service)
    {
    }

    public MainWindow(ControllerService service)
    {
        InitializeComponent();

        _service = service;
        ViewModel = new SettingsViewModel(service);
        DataContext = ViewModel;

        _factories = new Dictionary<PageId, Func<Control>>
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
            button.IsCheckedChanged += (s, e) =>
            {
                if (button.IsChecked == true)
                {
                    ShowPage(id);
                }
            };
        }

        //Ctrl+1 ... Ctrl+0 jump straight to a page
        KeyDown += (s, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Control)
            {
                return;
            }

            int index = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
                Key.D0 => 9,
                _ => -1,
            };

            var pages = Enum.GetValues<PageId>();

            if (index >= 0 && index < pages.Length)
            {
                Navigate(pages[index]);
                _navButtons[pages[index]].Focus();
                e.Handled = true;
            }
        };

        //Redraw code-colored parts after a theme switch
        CompanionApp.ThemeChanged += () =>
        {
            _shownStatus = null;
            UpdateLive();
        };

        Opened += (s, e) => StartLiveUpdates();
        NavHome.IsChecked = true;
    }

    public void Navigate(PageId page) => _navButtons[page].IsChecked = true;

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
        StartLiveUpdates();
    }

    private void ShowPage(PageId id)
    {
        if (!_pages.TryGetValue(id, out var page))
        {
            page = _factories[id]();
            _pages[id] = page;
        }

        PageHost.Content = page;
        Scroller.Offset = default;
        UpdateLive();
    }

    //The live view refreshes once per displayed frame, so it follows the monitor's refresh rate
    private void StartLiveUpdates()
    {
        if (_liveRunning)
        {
            return;
        }

        _liveRunning = true;
        RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            _liveRunning = false;
            return;
        }

        UpdateLive();
        RequestAnimationFrame(OnFrame);
    }

    private void UpdateLive()
    {
        var snapshot = SnapshotSource?.Invoke() ?? _service.GetSnapshot();

        if (snapshot.Status != _shownStatus)
        {
            UpdateStatus(snapshot.Status);
        }

        if (PageHost.Content is ILivePage live)
        {
            live.Update(snapshot);
        }
    }

    private void UpdateStatus(ControllerStatus status)
    {
        bool wasConnected = _shownStatus?.Connected ?? false;
        _shownStatus = status;

        if (!status.Connected)
        {
            StatusDot.Fill = this.Brush("StatusIdleBrush");
            StatusTitle.Text = "Not connected";
            StatusDetail.Text = "Plug in or pair your controller";
            StatusBatteryRow.IsVisible = false;
        }
        else
        {
            StatusDot.Fill = ConnectedDot;
            StatusTitle.Text = $"Connected · {status.Connection}";

            if (!status.HasBattery)
            {
                StatusDetail.Text = $"Emulating {status.Emulation}";
                StatusBatteryRow.IsVisible = false;
            }
            else
            {
                string charge = status.Battery switch
                {
                    BatteryState.Charging => " · charging",
                    BatteryState.Full => " · full",
                    _ => "",
                };

                StatusDetail.Text = status.BatteryRemaining is { } left
                    ? $"Battery {status.BatteryPercent}%, {BatteryEstimator.Describe(left).Replace("about ", "~")}"
                    : $"Battery {status.BatteryPercent}%{charge}";

                StatusBatteryRow.IsVisible = true;
                double width = StatusBatteryRow.Bounds.Width > 0 ? StatusBatteryRow.Bounds.Width : 170;
                StatusBatteryFill.Width = width * status.BatteryPercent / 100.0;
                StatusBatteryFill.Background = status.IsLowBattery(_service.Settings.Battery.LowThreshold)
                    ? LowBattery
                    : this.Brush("AccentBrush");
            }
        }

        AutomationProperties.SetName(StatusTitle, StatusTitle.Text);

        //Screen readers hear connects and disconnects
        if (wasConnected != status.Connected)
        {
            AutomationProperties.SetLiveSetting(StatusTitle, AutomationLiveSetting.Polite);
        }
    }

    private void Minimize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }
    }

    //The window has no system title bar: the top strips drag it, double-click maximizes
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    //Closing hides to the tray; the app keeps running for your games
    protected override void OnClosing(WindowClosingEventArgs e)
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
