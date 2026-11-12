using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;

namespace DualSenseCompanion.App;

public enum EmulationMode
{
    Xbox360,
    DualShock4,

    //No virtual controller and the DualSense stays visible: for games with built-in DualSense support
    Native,
}

public enum LightbarMode
{
    Static,
    Battery,
    Game, //color set by the game (DualShock 4 mode only; falls back to Static)
    Off,
}

public enum GyroMode
{
    Off,
    Mouse,
    RightStick,
}

public enum GyroActivation
{
    Always,
    WhileHeld,
    Toggle,
}

public enum GyroAxis
{
    Yaw,  //turn the controller left/right like a steering wheel lying flat
    Roll, //tilt the controller left/right
}

public sealed class StickSettings
{
    public float Deadzone { get; set; } = 0.05f;      //inner radial deadzone, 0-1
    public float OuterDeadzone { get; set; } = 0.02f; //reach full deflection slightly early
    public float AntiDeadzone { get; set; } = 0.0f;   //minimum output once outside the deadzone (counters game deadzones)
    public float Curve { get; set; } = 1.0f;          //1 = linear, >1 = finer control near center
}

public sealed class LightbarSettings
{
    public LightbarMode Mode { get; set; } = LightbarMode.Static;
    public byte Red { get; set; } = 0;
    public byte Green { get; set; } = 80;
    public byte Blue { get; set; } = 255;
    public int Brightness { get; set; } = 100; //percent
}

public sealed class BatterySettings
{
    public int LowThreshold { get; set; } = 20; //percent
    public bool PulseLightbarWhenLow { get; set; } = true;
    public bool NotifyWhenLow { get; set; } = true;
}

public sealed class GyroSettings
{
    public GyroMode Mode { get; set; } = GyroMode.Off;
    public GyroActivation Activation { get; set; } = GyroActivation.WhileHeld;
    public DsButtons ActivationButton { get; set; } = DsButtons.L2;
    public GyroAxis HorizontalAxis { get; set; } = GyroAxis.Yaw;
    public float MouseSensitivity { get; set; } = 12f;     //mouse pixels per degree of rotation
    public float StickFullSpeed { get; set; } = 180f;      //degrees per second for full stick deflection
    public float StickAntiDeadzone { get; set; } = 0.12f;  //jump past the game's own deadzone
    public float SmoothingThreshold { get; set; } = 1.0f;  //deg/s below which movement is softened to remove jitter
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
}

public sealed class TouchpadSettings
{
    public bool MouseEnabled { get; set; }
    public float Sensitivity { get; set; } = 1.0f;
    public bool TwoFingerScroll { get; set; } = true;
}

public sealed class Settings
{
    public EmulationMode Emulation { get; set; } = EmulationMode.Xbox360;
    public StickSettings LeftStick { get; set; } = new StickSettings();
    public StickSettings RightStick { get; set; } = new StickSettings();
    public float TriggerDeadzone { get; set; } = 0.0f;
    public int RumbleStrength { get; set; } = 100; //percent

    //DualSense button -> virtual controller button. Anything missing uses the default mapping.
    public Dictionary<DsButtons, PadButtons> ButtonMap { get; set; } = InputMapper.DefaultButtonMap();

    public LightbarSettings Lightbar { get; set; } = new LightbarSettings();
    public int PlayerNumber { get; set; } = 1; //0 = LEDs off, 1-4, 5 = all
    public BatterySettings Battery { get; set; } = new BatterySettings();
    public bool MicMuteButtonControlsWindows { get; set; } = true;
    public TriggerPreset LeftTriggerEffect { get; set; } = TriggerPreset.Off;
    public TriggerPreset RightTriggerEffect { get; set; } = TriggerPreset.Off;
    public GyroSettings Gyro { get; set; } = new GyroSettings();
    public bool StartMinimized { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public TouchpadSettings Touchpad { get; set; } = new TouchpadSettings();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualSenseCompanion");

    public static string FilePath { get; } = Path.Combine(Directory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Settings file is invalid, using defaults ({ex.Message}). A copy was saved as settings.invalid.json.");

            try
            {
                File.Copy(FilePath, Path.Combine(Directory, "settings.invalid.json"), overwrite: true);
            }
            catch
            {
                //Best effort only
            }
        }

        var defaults = new Settings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    //Fills gaps and clamps out-of-range values from hand-edited files
    public void Normalize()
    {
        LeftStick ??= new StickSettings();
        RightStick ??= new StickSettings();
        Lightbar ??= new LightbarSettings();
        Battery ??= new BatterySettings();
        Gyro ??= new GyroSettings();
        Touchpad ??= new TouchpadSettings();
        ButtonMap ??= new Dictionary<DsButtons, PadButtons>();

        foreach (var (source, target) in InputMapper.DefaultButtonMap())
        {
            ButtonMap.TryAdd(source, target);
        }

        foreach (var stick in new[] { LeftStick, RightStick })
        {
            stick.Deadzone = Math.Clamp(stick.Deadzone, 0f, 0.9f);
            stick.OuterDeadzone = Math.Clamp(stick.OuterDeadzone, 0f, 0.5f);
            stick.AntiDeadzone = Math.Clamp(stick.AntiDeadzone, 0f, 0.9f);
            stick.Curve = Math.Clamp(stick.Curve, 0.2f, 5f);
        }

        TriggerDeadzone = Math.Clamp(TriggerDeadzone, 0f, 0.9f);
        RumbleStrength = Math.Clamp(RumbleStrength, 0, 100);
        PlayerNumber = Math.Clamp(PlayerNumber, 0, 5);
        Lightbar.Brightness = Math.Clamp(Lightbar.Brightness, 0, 100);
        Battery.LowThreshold = Math.Clamp(Battery.LowThreshold, 0, 100);
        Gyro.MouseSensitivity = Math.Clamp(Gyro.MouseSensitivity, 0.1f, 200f);
        Gyro.StickFullSpeed = Math.Clamp(Gyro.StickFullSpeed, 10f, 2000f);
        Gyro.StickAntiDeadzone = Math.Clamp(Gyro.StickAntiDeadzone, 0f, 0.9f);
        Gyro.SmoothingThreshold = Math.Clamp(Gyro.SmoothingThreshold, 0f, 20f);
        Touchpad.Sensitivity = Math.Clamp(Touchpad.Sensitivity, 0.1f, 10f);
    }
}
