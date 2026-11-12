using System.Diagnostics;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.App;

//System tray icon and menu: every setting can be changed here and is saved immediately
public sealed class TrayApp : ApplicationContext
{
    private static readonly (string Name, float Mouse, float Stick)[] GyroSensitivities =
    {
        ("Low", 6f, 300f),
        ("Medium", 12f, 180f),
        ("High", 20f, 120f),
        ("Very high", 32f, 80f),
    };

    private static readonly (DsButtons Button, string Name)[] GyroButtons =
    {
        (DsButtons.L2, "L2"),
        (DsButtons.R2, "R2"),
        (DsButtons.L1, "L1"),
        (DsButtons.R1, "R1"),
        (DsButtons.L3, "L3"),
        (DsButtons.R3, "R3"),
        (DsButtons.TouchpadTouch, "Finger on touchpad"),
    };

    private readonly ControllerService _service;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Control _invoker;
    private readonly Icon? _appIcon;
    private LogWindow? _logWindow;

    private Settings S => _service.Settings;

    public TrayApp(ControllerService service)
    {
        _service = service;

        _invoker = new Control();
        _invoker.CreateControl();

        try
        {
            _appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch
        {
            _appIcon = null;
        }

        _menu = new ContextMenuStrip();
        _menu.Opening += (s, e) =>
        {
            BuildMenu();
            e.Cancel = false;
        };

        _icon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true,
        };

        _icon.DoubleClick += (s, e) => ShowLog();

        _service.StatusChanged += status => OnUi(() => UpdateTooltip(status));
        _service.Notification += message => OnUi(() => _icon.ShowBalloonTip(3000, "DualSense Companion", message, ToolTipIcon.Info));

        UpdateTooltip(_service.Status);
        _icon.ShowBalloonTip(3000, "DualSense Companion", "Running in the system tray. Right-click the icon for options.", ToolTipIcon.Info);
    }

    private void OnUi(Action action)
    {
        if (_invoker.IsDisposed)
        {
            return;
        }

        try
        {
            _invoker.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            //Shutting down
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
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();

        var status = new ToolStripMenuItem(_service.Status.Describe()) { Enabled = false };
        _menu.Items.Add(status);
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(Sub("Emulate",
            Radio("Xbox 360 controller", EmulationMode.Xbox360, S.Emulation, v => S.Emulation = v),
            Radio("DualShock 4 (gyro + touchpad passthrough)", EmulationMode.DualShock4, S.Emulation, v => S.Emulation = v)));

        _menu.Items.Add(BuildLightbarMenu());

        _menu.Items.Add(Sub("Player LEDs",
            Radio("Off", 0, S.PlayerNumber, v => S.PlayerNumber = v),
            Radio("Player 1", 1, S.PlayerNumber, v => S.PlayerNumber = v),
            Radio("Player 2", 2, S.PlayerNumber, v => S.PlayerNumber = v),
            Radio("Player 3", 3, S.PlayerNumber, v => S.PlayerNumber = v),
            Radio("Player 4", 4, S.PlayerNumber, v => S.PlayerNumber = v),
            Radio("All", 5, S.PlayerNumber, v => S.PlayerNumber = v)));

        _menu.Items.Add(Sub("Adaptive triggers",
            Sub("L2", TriggerItems(S.LeftTriggerEffect, v => S.LeftTriggerEffect = v)),
            Sub("R2", TriggerItems(S.RightTriggerEffect, v => S.RightTriggerEffect = v))));

        _menu.Items.Add(BuildGyroMenu());

        _menu.Items.Add(Sub("Touchpad",
            Check("Use as mouse (click = left, two-finger click = right)", S.Touchpad.MouseEnabled, v => S.Touchpad.MouseEnabled = v),
            Check("Two-finger scroll", S.Touchpad.TwoFingerScroll, v => S.Touchpad.TwoFingerScroll = v)));

        _menu.Items.Add(Check("Mute button controls Windows microphone", S.MicMuteButtonControlsWindows, v => S.MicMuteButtonControlsWindows = v));

        _menu.Items.Add(Sub("Rumble",
            Radio("Off", 0, S.RumbleStrength, v => S.RumbleStrength = v),
            Radio("25%", 25, S.RumbleStrength, v => S.RumbleStrength = v),
            Radio("50%", 50, S.RumbleStrength, v => S.RumbleStrength = v),
            Radio("75%", 75, S.RumbleStrength, v => S.RumbleStrength = v),
            Radio("100%", 100, S.RumbleStrength, v => S.RumbleStrength = v)));

        _menu.Items.Add(Sub("Low battery",
            Check("Pulse lightbar red", S.Battery.PulseLightbarWhenLow, v => S.Battery.PulseLightbarWhenLow = v),
            Check("Show notification", S.Battery.NotifyWhenLow, v => S.Battery.NotifyWhenLow = v),
            new ToolStripSeparator(),
            Radio("Warn at 10%", 10, S.Battery.LowThreshold, v => S.Battery.LowThreshold = v),
            Radio("Warn at 20%", 20, S.Battery.LowThreshold, v => S.Battery.LowThreshold = v),
            Radio("Warn at 30%", 30, S.Battery.LowThreshold, v => S.Battery.LowThreshold = v)));

        _menu.Items.Add(new ToolStripSeparator());

        bool autostart = SafeAutostartEnabled();
        _menu.Items.Add(Item("Start with Windows", () => ToggleAutostart(!autostart), isChecked: autostart));
        _menu.Items.Add(Item("Open settings file (deadzones, button mapping...)", OpenSettingsFile));
        _menu.Items.Add(Item("Reload settings file", ReloadSettings));
        _menu.Items.Add(Item("Show log", ShowLog));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Exit", ExitThread));
    }

    private ToolStripMenuItem BuildLightbarMenu()
    {
        var l = S.Lightbar;

        return Sub("Lightbar",
            Item("Custom color...", PickLightbarColor, isChecked: l.Mode == LightbarMode.Static),
            Radio("Battery level (green to red)", LightbarMode.Battery, l.Mode, v => l.Mode = v),
            Radio("Set by game (DualShock 4 mode)", LightbarMode.Game, l.Mode, v => l.Mode = v),
            Radio("Off", LightbarMode.Off, l.Mode, v => l.Mode = v),
            new ToolStripSeparator(),
            Sub("Brightness",
                Radio("100%", 100, l.Brightness, v => l.Brightness = v),
                Radio("75%", 75, l.Brightness, v => l.Brightness = v),
                Radio("50%", 50, l.Brightness, v => l.Brightness = v),
                Radio("25%", 25, l.Brightness, v => l.Brightness = v)));
    }

    private ToolStripMenuItem BuildGyroMenu()
    {
        var g = S.Gyro;

        var holdItems = GyroButtons.Select(b => (ToolStripItem)Item(b.Name, () =>
        {
            g.Activation = GyroActivation.WhileHeld;
            g.ActivationButton = b.Button;
            Apply();
        }, isChecked: g.Activation == GyroActivation.WhileHeld && g.ActivationButton == b.Button)).ToArray();

        var toggleItems = GyroButtons.Select(b => (ToolStripItem)Item(b.Name, () =>
        {
            g.Activation = GyroActivation.Toggle;
            g.ActivationButton = b.Button;
            Apply();
        }, isChecked: g.Activation == GyroActivation.Toggle && g.ActivationButton == b.Button)).ToArray();

        var sensitivityItems = GyroSensitivities.Select(level => (ToolStripItem)Item(level.Name, () =>
        {
            g.MouseSensitivity = level.Mouse;
            g.StickFullSpeed = level.Stick;
            Apply();
        }, isChecked: Math.Abs(g.MouseSensitivity - level.Mouse) < 0.01f)).ToArray();

        return Sub("Gyro aiming",
            Radio("Off", GyroMode.Off, g.Mode, v => g.Mode = v),
            Radio("Mouse (best precision)", GyroMode.Mouse, g.Mode, v => g.Mode = v),
            Radio("Right stick (works in every game)", GyroMode.RightStick, g.Mode, v => g.Mode = v),
            new ToolStripSeparator(),
            Sub("Activation",
                Radio("Always on", GyroActivation.Always, g.Activation, v => g.Activation = v),
                Sub("While holding", holdItems),
                Sub("Toggle with", toggleItems)),
            Sub("Sensitivity", sensitivityItems),
            Sub("Horizontal motion",
                Radio("Turn (yaw)", GyroAxis.Yaw, g.HorizontalAxis, v => g.HorizontalAxis = v),
                Radio("Tilt (roll)", GyroAxis.Roll, g.HorizontalAxis, v => g.HorizontalAxis = v)),
            Check("Invert horizontal", g.InvertX, v => g.InvertX = v),
            Check("Invert vertical", g.InvertY, v => g.InvertY = v),
            new ToolStripSeparator(),
            Item("Calibrate (put the controller down and don't touch it)", _service.RecalibrateGyro));
    }

    private ToolStripItem[] TriggerItems(TriggerPreset current, Action<TriggerPreset> set) =>
        new ToolStripItem[]
        {
            Radio("Off", TriggerPreset.Off, current, set),
            Radio("Soft resistance", TriggerPreset.Soft, current, set),
            Radio("Medium resistance", TriggerPreset.Medium, current, set),
            Radio("Hard resistance", TriggerPreset.Hard, current, set),
            Radio("Weapon (click at mid-pull)", TriggerPreset.Weapon, current, set),
            Radio("Vibrate", TriggerPreset.Vibrate, current, set),
        };

    private void PickLightbarColor()
    {
        var l = S.Lightbar;

        using var dialog = new ColorDialog
        {
            Color = Color.FromArgb(l.Red, l.Green, l.Blue),
            FullOpen = true,
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            l.Red = dialog.Color.R;
            l.Green = dialog.Color.G;
            l.Blue = dialog.Color.B;
        }

        l.Mode = LightbarMode.Static;
        Apply();
    }

    private static bool SafeAutostartEnabled()
    {
        try
        {
            return Autostart.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private void ToggleAutostart(bool enabled)
    {
        try
        {
            Autostart.Set(enabled);
            Console.WriteLine(enabled ? "Start with Windows enabled." : "Start with Windows disabled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not change startup setting: {ex.Message}");
        }
    }

    private void OpenSettingsFile()
    {
        S.Save();

        try
        {
            Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true });
        }
        catch
        {
            //No app associated with .json
            Process.Start("notepad.exe", $"\"{Settings.FilePath}\"");
        }
    }

    private void ReloadSettings()
    {
        _service.ReplaceSettings(Settings.Load());
        Console.WriteLine("Settings reloaded.");
        _icon.ShowBalloonTip(2000, "DualSense Companion", "Settings reloaded.", ToolTipIcon.Info);
    }

    private void ShowLog()
    {
        if (_logWindow == null || _logWindow.IsDisposed)
        {
            _logWindow = new LogWindow(_appIcon);
        }

        _logWindow.Show();
        _logWindow.Activate();
    }

    private ToolStripMenuItem Item(string text, Action onClick, bool isChecked = false)
    {
        var item = new ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (s, e) => onClick();
        return item;
    }

    private ToolStripMenuItem Radio<T>(string text, T value, T current, Action<T> set) =>
        Item(text, () =>
        {
            set(value);
            Apply();
        }, isChecked: EqualityComparer<T>.Default.Equals(value, current));

    private ToolStripMenuItem Check(string text, bool current, Action<bool> set) =>
        Item(text, () =>
        {
            set(!current);
            Apply();
        }, isChecked: current);

    private static ToolStripMenuItem Sub(string text, params ToolStripItem[] items)
    {
        var item = new ToolStripMenuItem(text);
        item.DropDownItems.AddRange(items);
        return item;
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _logWindow?.Dispose();
        _invoker.Dispose();
        base.ExitThreadCore();
    }
}
