using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;

namespace DualSenseCompanion.UI;

public sealed record Option<T>(T Value, string Name)
{
    //Shown in the closed ComboBox
    public override string ToString() => Name;
}

public sealed class ButtonMapEntry : INotifyPropertyChanged
{
    private readonly SettingsViewModel _owner;

    public DsButtons Source { get; }
    public string Name { get; }

    public ButtonMapEntry(SettingsViewModel owner, DsButtons source, string name)
    {
        _owner = owner;
        Source = source;
        Name = name;
    }

    public PadButtons Target
    {
        get => _owner.Settings.ButtonMap.TryGetValue(Source, out var target) ? target : PadButtons.None;
        set
        {
            _owner.Settings.ButtonMap[Source] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Target)));
            _owner.ScheduleSave();
        }
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Target)));

    public event PropertyChangedEventHandler? PropertyChanged;
}

//Two-way bridge between the UI and Settings. Changes take effect immediately; the file is saved shortly after.
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ControllerService _service;
    private readonly DispatcherTimer _saveTimer;

    public Settings Settings => _service.Settings;

    public SettingsViewModel(ControllerService service)
    {
        _service = service;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (s, e) =>
        {
            _saveTimer.Stop();
            _service.ApplySettings();
        };

        ButtonMap = new ObservableCollection<ButtonMapEntry>(
            ButtonNames.Select(b => new ButtonMapEntry(this, b.Value, b.Name)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    //Call after Settings was replaced (e.g. reloaded from disk)
    public void RefreshAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        foreach (var entry in ButtonMap)
        {
            entry.Refresh();
        }
    }

    private void Changed(Action apply, [CallerMemberName] string? name = null, params string[] alsoChanged)
    {
        apply();
        _service.RequestOutput();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        foreach (var other in alsoChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(other));
        }

        ScheduleSave();
    }

    // ---- Lists for drop-downs ----

    public static IReadOnlyList<Option<DsButtons>> ButtonNames { get; } = new[]
    {
        new Option<DsButtons>(DsButtons.Cross, "Cross ✕"),
        new Option<DsButtons>(DsButtons.Circle, "Circle ○"),
        new Option<DsButtons>(DsButtons.Square, "Square □"),
        new Option<DsButtons>(DsButtons.Triangle, "Triangle △"),
        new Option<DsButtons>(DsButtons.L1, "L1"),
        new Option<DsButtons>(DsButtons.R1, "R1"),
        new Option<DsButtons>(DsButtons.L3, "L3 (left stick click)"),
        new Option<DsButtons>(DsButtons.R3, "R3 (right stick click)"),
        new Option<DsButtons>(DsButtons.Create, "Create"),
        new Option<DsButtons>(DsButtons.Options, "Options"),
        new Option<DsButtons>(DsButtons.PS, "PS button"),
        new Option<DsButtons>(DsButtons.Touchpad, "Touchpad click"),
        new Option<DsButtons>(DsButtons.Mute, "Mute button"),
        new Option<DsButtons>(DsButtons.DpadUp, "D-pad up"),
        new Option<DsButtons>(DsButtons.DpadDown, "D-pad down"),
        new Option<DsButtons>(DsButtons.DpadLeft, "D-pad left"),
        new Option<DsButtons>(DsButtons.DpadRight, "D-pad right"),
    };

    public static IReadOnlyList<Option<PadButtons>> TargetNames { get; } = new[]
    {
        new Option<PadButtons>(PadButtons.None, "Not mapped"),
        new Option<PadButtons>(PadButtons.A, "A / Cross"),
        new Option<PadButtons>(PadButtons.B, "B / Circle"),
        new Option<PadButtons>(PadButtons.X, "X / Square"),
        new Option<PadButtons>(PadButtons.Y, "Y / Triangle"),
        new Option<PadButtons>(PadButtons.LeftShoulder, "LB / L1"),
        new Option<PadButtons>(PadButtons.RightShoulder, "RB / R1"),
        new Option<PadButtons>(PadButtons.LeftThumb, "Left stick click"),
        new Option<PadButtons>(PadButtons.RightThumb, "Right stick click"),
        new Option<PadButtons>(PadButtons.Back, "Back / Share"),
        new Option<PadButtons>(PadButtons.Start, "Start / Options"),
        new Option<PadButtons>(PadButtons.Guide, "Guide / PS"),
        new Option<PadButtons>(PadButtons.Up, "D-pad up"),
        new Option<PadButtons>(PadButtons.Down, "D-pad down"),
        new Option<PadButtons>(PadButtons.Left, "D-pad left"),
        new Option<PadButtons>(PadButtons.Right, "D-pad right"),
        new Option<PadButtons>(PadButtons.Touchpad, "Touchpad click (DualShock 4 only)"),
    };

    public static IReadOnlyList<Option<DsButtons>> GyroButtonNames { get; } = new[]
    {
        new Option<DsButtons>(DsButtons.L2, "L2"),
        new Option<DsButtons>(DsButtons.R2, "R2"),
        new Option<DsButtons>(DsButtons.L1, "L1"),
        new Option<DsButtons>(DsButtons.R1, "R1"),
        new Option<DsButtons>(DsButtons.L3, "L3"),
        new Option<DsButtons>(DsButtons.R3, "R3"),
        new Option<DsButtons>(DsButtons.TouchpadTouch, "Finger on touchpad"),
    };

    public ObservableCollection<ButtonMapEntry> ButtonMap { get; }

    public void ResetButtonMap()
    {
        Settings.ButtonMap = InputMapper.DefaultButtonMap();

        foreach (var entry in ButtonMap)
        {
            entry.Refresh();
        }

        ScheduleSave();
    }

    // ---- General ----

    public bool IsNative => Settings.Emulation == EmulationMode.Native;

    public EmulationMode Emulation
    {
        get => Settings.Emulation;
        set
        {
            Settings.Emulation = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Emulation)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNative)));

            //Recreate the virtual controller right away
            _saveTimer.Stop();
            _service.ApplySettings();
        }
    }

    public double RumbleStrength
    {
        get => Settings.RumbleStrength;
        set => Changed(() => Settings.RumbleStrength = (int)Math.Round(value));
    }

    public bool MicMuteButton
    {
        get => Settings.MicMuteButtonControlsWindows;
        set => Changed(() => Settings.MicMuteButtonControlsWindows = value);
    }

    public bool StartMinimized
    {
        get => Settings.StartMinimized;
        set => Changed(() => Settings.StartMinimized = value);
    }

    public bool CheckForUpdates
    {
        get => Settings.CheckForUpdates;
        set => Changed(() => Settings.CheckForUpdates = value);
    }

    private UpdateInfo? _update;
    private string _updateStatus = $"You're on version {UpdateChecker.CurrentVersion}.";

    public string UpdateStatus => _updateStatus;
    public bool HasUpdate => _update != null;
    public string? UpdateUrl => _update?.Url;

    public void SetUpdate(UpdateInfo? update, bool checkedNow)
    {
        _update = update;
        _updateStatus = update != null
            ? $"Version {update.Version} is available (you have {UpdateChecker.CurrentVersion})."
            : checkedNow
                ? $"You're on the latest version ({UpdateChecker.CurrentVersion})."
                : $"You're on version {UpdateChecker.CurrentVersion}.";

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateStatus)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUpdate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateUrl)));
    }

    public async Task CheckForUpdatesNowAsync()
    {
        _updateStatus = "Checking...";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateStatus)));
        SetUpdate(await UpdateChecker.CheckAsync(), checkedNow: true);
    }

    public bool StartWithWindows
    {
        get
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
        set
        {
            try
            {
                Autostart.Set(value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not change startup setting: {ex.Message}");
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartWithWindows)));
        }
    }

    // ---- Lightbar ----

    public LightbarMode LightbarMode
    {
        get => Settings.Lightbar.Mode;
        set => Changed(() => Settings.Lightbar.Mode = value, nameof(LightbarMode), nameof(ShowEffectSpeed));
    }

    public bool ShowEffectSpeed => Settings.Lightbar.Mode is LightbarMode.Breathing or LightbarMode.Rainbow;

    public double LightbarEffectSpeed
    {
        get => Settings.Lightbar.EffectSpeed;
        set => Changed(() => Settings.Lightbar.EffectSpeed = (float)Math.Round(value, 1));
    }

    public bool PulseRedWhenMicMuted
    {
        get => Settings.Lightbar.PulseRedWhenMicMuted;
        set => Changed(() => Settings.Lightbar.PulseRedWhenMicMuted = value);
    }

    public double LightbarRed
    {
        get => Settings.Lightbar.Red;
        set => Changed(() => Settings.Lightbar.Red = (byte)Math.Round(value), nameof(LightbarRed), nameof(LightbarPreview), nameof(LightbarHex));
    }

    public double LightbarGreen
    {
        get => Settings.Lightbar.Green;
        set => Changed(() => Settings.Lightbar.Green = (byte)Math.Round(value), nameof(LightbarGreen), nameof(LightbarPreview), nameof(LightbarHex));
    }

    public double LightbarBlue
    {
        get => Settings.Lightbar.Blue;
        set => Changed(() => Settings.Lightbar.Blue = (byte)Math.Round(value), nameof(LightbarBlue), nameof(LightbarPreview), nameof(LightbarHex));
    }

    public SolidColorBrush LightbarPreview =>
        new SolidColorBrush(Color.FromRgb(Settings.Lightbar.Red, Settings.Lightbar.Green, Settings.Lightbar.Blue));

    public string LightbarHex => $"#{Settings.Lightbar.Red:X2}{Settings.Lightbar.Green:X2}{Settings.Lightbar.Blue:X2}";

    public void SetLightbarColor(Color color)
    {
        Settings.Lightbar.Red = color.R;
        Settings.Lightbar.Green = color.G;
        Settings.Lightbar.Blue = color.B;
        Settings.Lightbar.Mode = LightbarMode.Static;
        Changed(() => { }, nameof(LightbarRed), nameof(LightbarGreen), nameof(LightbarBlue), nameof(LightbarPreview), nameof(LightbarHex), nameof(LightbarMode));
    }

    public double LightbarBrightness
    {
        get => Settings.Lightbar.Brightness;
        set => Changed(() => Settings.Lightbar.Brightness = (int)Math.Round(value));
    }

    public int PlayerNumber
    {
        get => Settings.PlayerNumber;
        set => Changed(() => Settings.PlayerNumber = value);
    }

    public bool PulseWhenLow
    {
        get => Settings.Battery.PulseLightbarWhenLow;
        set => Changed(() => Settings.Battery.PulseLightbarWhenLow = value);
    }

    public bool NotifyWhenLow
    {
        get => Settings.Battery.NotifyWhenLow;
        set => Changed(() => Settings.Battery.NotifyWhenLow = value);
    }

    public double LowBatteryThreshold
    {
        get => Settings.Battery.LowThreshold;
        set => Changed(() => Settings.Battery.LowThreshold = (int)Math.Round(value / 5) * 5);
    }

    // ---- Triggers ----

    public TriggerPreset LeftTriggerEffect
    {
        get => Settings.LeftTriggerEffect;
        set => Changed(() => Settings.LeftTriggerEffect = value);
    }

    public TriggerPreset RightTriggerEffect
    {
        get => Settings.RightTriggerEffect;
        set => Changed(() => Settings.RightTriggerEffect = value);
    }

    public double TriggerDeadzone
    {
        get => Settings.TriggerDeadzone;
        set => Changed(() => Settings.TriggerDeadzone = (float)value);
    }

    // ---- Sticks ----

    public double LeftDeadzone { get => Settings.LeftStick.Deadzone; set => Changed(() => Settings.LeftStick.Deadzone = (float)value); }
    public double LeftOuterDeadzone { get => Settings.LeftStick.OuterDeadzone; set => Changed(() => Settings.LeftStick.OuterDeadzone = (float)value); }
    public double LeftAntiDeadzone { get => Settings.LeftStick.AntiDeadzone; set => Changed(() => Settings.LeftStick.AntiDeadzone = (float)value); }
    public double LeftCurve { get => Settings.LeftStick.Curve; set => Changed(() => Settings.LeftStick.Curve = (float)value); }

    public double RightDeadzone { get => Settings.RightStick.Deadzone; set => Changed(() => Settings.RightStick.Deadzone = (float)value); }
    public double RightOuterDeadzone { get => Settings.RightStick.OuterDeadzone; set => Changed(() => Settings.RightStick.OuterDeadzone = (float)value); }
    public double RightAntiDeadzone { get => Settings.RightStick.AntiDeadzone; set => Changed(() => Settings.RightStick.AntiDeadzone = (float)value); }
    public double RightCurve { get => Settings.RightStick.Curve; set => Changed(() => Settings.RightStick.Curve = (float)value); }

    // ---- Gyro ----

    public GyroMode GyroMode
    {
        get => Settings.Gyro.Mode;
        set => Changed(() => Settings.Gyro.Mode = value, nameof(GyroMode), nameof(GyroEnabled), nameof(GyroIsMouse), nameof(GyroIsStick));
    }

    public bool GyroEnabled => Settings.Gyro.Mode != GyroMode.Off;
    public bool GyroIsMouse => Settings.Gyro.Mode == GyroMode.Mouse;
    public bool GyroIsStick => Settings.Gyro.Mode == GyroMode.RightStick;

    public GyroActivation GyroActivation
    {
        get => Settings.Gyro.Activation;
        set => Changed(() => Settings.Gyro.Activation = value, nameof(GyroActivation), nameof(GyroNeedsButton));
    }

    public bool GyroNeedsButton => Settings.Gyro.Activation != GyroActivation.Always;

    public DsButtons GyroButton
    {
        get => Settings.Gyro.ActivationButton;
        set => Changed(() => Settings.Gyro.ActivationButton = value);
    }

    public GyroAxis GyroHorizontalAxis
    {
        get => Settings.Gyro.HorizontalAxis;
        set => Changed(() => Settings.Gyro.HorizontalAxis = value);
    }

    public double GyroMouseSensitivity
    {
        get => Settings.Gyro.MouseSensitivity;
        set => Changed(() => Settings.Gyro.MouseSensitivity = (float)Math.Round(value, 1));
    }

    public double GyroStickSpeed
    {
        get => Settings.Gyro.StickFullSpeed;
        set => Changed(() => Settings.Gyro.StickFullSpeed = (float)Math.Round(value / 5) * 5);
    }

    public double GyroStickAntiDeadzone
    {
        get => Settings.Gyro.StickAntiDeadzone;
        set => Changed(() => Settings.Gyro.StickAntiDeadzone = (float)value);
    }

    public double GyroSmoothing
    {
        get => Settings.Gyro.SmoothingThreshold;
        set => Changed(() => Settings.Gyro.SmoothingThreshold = (float)Math.Round(value, 1));
    }

    public bool GyroInvertX
    {
        get => Settings.Gyro.InvertX;
        set => Changed(() => Settings.Gyro.InvertX = value);
    }

    public bool GyroInvertY
    {
        get => Settings.Gyro.InvertY;
        set => Changed(() => Settings.Gyro.InvertY = value);
    }

    // ---- Touchpad ----

    public bool TouchpadMouse
    {
        get => Settings.Touchpad.MouseEnabled;
        set => Changed(() => Settings.Touchpad.MouseEnabled = value);
    }

    public bool TouchpadScroll
    {
        get => Settings.Touchpad.TwoFingerScroll;
        set => Changed(() => Settings.Touchpad.TwoFingerScroll = value);
    }

    public double TouchpadSensitivity
    {
        get => Settings.Touchpad.Sensitivity;
        set => Changed(() => Settings.Touchpad.Sensitivity = (float)Math.Round(value, 1));
    }
}
