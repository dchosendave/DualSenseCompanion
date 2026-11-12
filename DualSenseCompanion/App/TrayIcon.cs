using System.Windows.Threading;
using DualSenseCompanion.UI;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DualSenseCompanion.App;

//System tray icon: keeps the app reachable while the window is closed, with a few quick actions
public sealed class TrayIcon : IDisposable
{
    private readonly ControllerService _service;
    private readonly Dispatcher _dispatcher;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;

    public event Action? OpenRequested;
    public event Action<PageId>? PageRequested;
    public event Action? SettingsChanged;
    public event Action? ExitRequested;

    private Settings S => _service.Settings;

    public TrayIcon(ControllerService service, Dispatcher dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;

        Drawing.Icon? appIcon = null;

        try
        {
            appIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch
        {
            //Fall back to the default icon
        }

        _menu = new WinForms.ContextMenuStrip();
        _menu.Opening += (s, e) =>
        {
            BuildMenu();
            e.Cancel = false;
        };

        _icon = new WinForms.NotifyIcon
        {
            Icon = appIcon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true,
        };

        _icon.MouseClick += (s, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };

        _service.StatusChanged += status => OnUi(() => UpdateTooltip(status));
        _service.Notification += message => OnUi(() => ShowMessage(message));

        UpdateTooltip(_service.Status);
    }

    public void ShowMessage(string message) =>
        _icon.ShowBalloonTip(3000, "DualSense Companion", message, WinForms.ToolTipIcon.Info);

    private void OnUi(Action action)
    {
        if (!_dispatcher.HasShutdownStarted)
        {
            _dispatcher.BeginInvoke(action);
        }
    }

    private void UpdateTooltip(ControllerStatus status)
    {
        string text = $"DualSense Companion\n{status.Describe()}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void Apply()
    {
        _service.ApplySettings();
        SettingsChanged?.Invoke();
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();

        var open = Item("Open DualSense Companion", () => OpenRequested?.Invoke());
        open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
        _menu.Items.Add(open);
        _menu.Items.Add(new WinForms.ToolStripMenuItem(_service.Status.Describe()) { Enabled = false });
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        _menu.Items.Add(Sub("Games see a",
            Radio("Xbox 360 controller", EmulationMode.Xbox360, S.Emulation, v => S.Emulation = v),
            Radio("DualShock 4 controller", EmulationMode.DualShock4, S.Emulation, v => S.Emulation = v),
            Radio("Native DualSense (game supports it)", EmulationMode.Native, S.Emulation, v => S.Emulation = v)));

        _menu.Items.Add(Sub("Gyro aiming",
            Radio("Off", GyroMode.Off, S.Gyro.Mode, v => S.Gyro.Mode = v),
            Radio("Mouse", GyroMode.Mouse, S.Gyro.Mode, v => S.Gyro.Mode = v),
            Radio("Right stick", GyroMode.RightStick, S.Gyro.Mode, v => S.Gyro.Mode = v),
            new WinForms.ToolStripSeparator(),
            Item("Calibrate (put the controller down)", _service.RecalibrateGyro)));

        _menu.Items.Add(Check("Touchpad as mouse", S.Touchpad.MouseEnabled, v => S.Touchpad.MouseEnabled = v));
        _menu.Items.Add(Check("Mute button controls Windows microphone", S.MicMuteButtonControlsWindows, v => S.MicMuteButtonControlsWindows = v));

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(Item("Show log", () => PageRequested?.Invoke(PageId.Log)));
        _menu.Items.Add(Item("Exit", () => ExitRequested?.Invoke()));
    }

    private static WinForms.ToolStripMenuItem Item(string text, Action onClick, bool isChecked = false)
    {
        var item = new WinForms.ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (s, e) => onClick();
        return item;
    }

    private WinForms.ToolStripMenuItem Radio<T>(string text, T value, T current, Action<T> set) =>
        Item(text, () =>
        {
            set(value);
            Apply();
        }, isChecked: EqualityComparer<T>.Default.Equals(value, current));

    private WinForms.ToolStripMenuItem Check(string text, bool current, Action<bool> set) =>
        Item(text, () =>
        {
            set(!current);
            Apply();
        }, isChecked: current);

    private static WinForms.ToolStripMenuItem Sub(string text, params WinForms.ToolStripItem[] items)
    {
        var item = new WinForms.ToolStripMenuItem(text);
        item.DropDownItems.AddRange(items);
        return item;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
