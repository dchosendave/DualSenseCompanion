using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;
using DualSenseCompanion.Features;

namespace DualSenseCompanion.Tests;

public class StickTests
{
    private static readonly StickSettings NoDeadzone = new StickSettings { Deadzone = 0, OuterDeadzone = 0, AntiDeadzone = 0, Curve = 1 };

    [Fact]
    public void CenterIsExactlyZero()
    {
        Assert.Equal((0f, 0f), InputMapper.ApplyStick(128, 128, NoDeadzone));
    }

    [Fact]
    public void ExtremesReachFullRangeAndYIsUpPositive()
    {
        Assert.Equal(1f, InputMapper.ApplyStick(255, 128, NoDeadzone).X, 3);
        Assert.Equal(-1f, InputMapper.ApplyStick(0, 128, NoDeadzone).X, 3);
        Assert.Equal(1f, InputMapper.ApplyStick(128, 0, NoDeadzone).Y, 3);
        Assert.Equal(-1f, InputMapper.ApplyStick(128, 255, NoDeadzone).Y, 3);

        //Corners are kept on the unit circle (radial processing)
        var (x, y) = InputMapper.ApplyStick(255, 0, NoDeadzone);
        Assert.Equal(0.7071f, x, 3);
        Assert.Equal(0.7071f, y, 3);
    }

    [Fact]
    public void DeadzoneRemovesSmallMovement()
    {
        var s = new StickSettings { Deadzone = 0.1f };
        Assert.Equal((0f, 0f), InputMapper.ApplyStick(135, 128, s));
    }

    [Fact]
    public void OutputIsContinuousOutsideDeadzone()
    {
        var s = new StickSettings { Deadzone = 0.1f, OuterDeadzone = 0 };
        var (x, _) = InputMapper.ApplyStick(128 + 14, 128, s); //just past 10%
        Assert.InRange(x, 0f, 0.02f);
    }

    [Fact]
    public void AntiDeadzoneJumpsToMinimum()
    {
        var s = new StickSettings { Deadzone = 0.05f, AntiDeadzone = 0.2f, OuterDeadzone = 0 };
        var (x, _) = InputMapper.ApplyStick(128 + 8, 128, s);
        Assert.InRange(x, 0.2f, 0.22f);
    }

    [Fact]
    public void TriggerDeadzone()
    {
        Assert.Equal(0f, InputMapper.ApplyTrigger(20, 0.1f));
        Assert.Equal(1f, InputMapper.ApplyTrigger(255, 0.1f), 3);
    }

    [Theory]
    [InlineData(-1f, -32767)]
    [InlineData(0f, 0)]
    [InlineData(1f, 32767)]
    public void XboxAxisConversion(float value, short expected)
    {
        Assert.Equal(expected, Xbox360Pad.ToAxis(value));
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0f, 128)]
    [InlineData(1f, 255)]
    public void Ds4AxisConversion(float value, byte expected)
    {
        Assert.Equal(expected, DualShock4Pad.ToStick(value));
    }
}

public class ButtonMapTests
{
    [Fact]
    public void DefaultMappingMatchesOriginalApp()
    {
        var settings = new Settings();
        var ds = new DualSenseState { Buttons = DsButtons.Cross | DsButtons.Create | DsButtons.PS | DsButtons.DpadLeft };

        var pad = InputMapper.Map(ds, settings, DsButtons.None);

        Assert.Equal(PadButtons.A | PadButtons.Back | PadButtons.Guide | PadButtons.Left, pad.Buttons);
    }

    [Fact]
    public void RemapAndConsumedButtons()
    {
        var settings = new Settings();
        settings.ButtonMap[DsButtons.Cross] = PadButtons.B;
        settings.ButtonMap[DsButtons.Mute] = PadButtons.Back;

        var ds = new DualSenseState { Buttons = DsButtons.Cross | DsButtons.Mute | DsButtons.Touchpad };
        var pad = InputMapper.Map(ds, settings, consumed: DsButtons.Mute | DsButtons.Touchpad);

        Assert.Equal(PadButtons.B, pad.Buttons);
    }

    [Fact]
    public void SettingsRoundTripThroughJsonKeepsMapping()
    {
        var settings = new Settings();
        settings.ButtonMap[DsButtons.Square] = PadButtons.Y;

        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        string json = System.Text.Json.JsonSerializer.Serialize(settings, options);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Settings>(json, options)!;
        loaded.Normalize();

        Assert.Contains("\"Square\": \"Y\"", json.Replace("\":\"", "\": \""));
        Assert.Equal(PadButtons.Y, loaded.ButtonMap[DsButtons.Square]);
        Assert.Equal(PadButtons.A, loaded.ButtonMap[DsButtons.Cross]);
    }
}

public class Ds4ReportTests
{
    [Fact]
    public void BuildsButtonsDpadAndTouch()
    {
        var ds = new DualSenseState
        {
            HasExtendedData = true,
            GyroX = 100,
            AccelZ = -200,
            Battery = BatteryState.Discharging,
            BatteryPercent = 55,
            Touch1 = new TouchPoint { Active = true, Id = 3, X = 1000, Y = 1079 },
        };

        var pad = new PadState
        {
            Buttons = PadButtons.A | PadButtons.Guide | PadButtons.Up | PadButtons.Right,
            LeftTrigger = 1f,
            Source = ds,
        };

        var r = new byte[DualShock4Pad.ReportSize];
        ushort ts = 0;
        uint? last = null;
        byte counter = 0;
        DualShock4Pad.BuildReport(pad, r, ref ts, ref last, ref counter);

        Assert.Equal(128, r[0]);
        Assert.Equal(1, r[4] & 0x0F);              //d-pad NE
        Assert.NotEqual(0, r[4] & (1 << 5));       //Cross
        Assert.NotEqual(0, r[5] & (1 << 2));       //L2 digital
        Assert.Equal(0x01, r[6]);                  //PS
        Assert.Equal(255, r[7]);                   //L2 analog
        Assert.Equal(100, BitConverter.ToInt16(r, 12));
        Assert.Equal(-200, BitConverter.ToInt16(r, 22));
        Assert.Equal(5, r[29]);                    //battery level, no cable
        Assert.Equal(1, r[32]);                    //one touch packet
        Assert.Equal(3, r[34]);                    //touch 1 active, id 3
        Assert.Equal(0x80, r[38]);                 //touch 2 inactive
        int y = (r[36] >> 4) | (r[37] << 4);
        Assert.Equal(942, y);                      //scaled to DS4 height
    }

    [Fact]
    public void TouchCanBeSuppressed()
    {
        var ds = new DualSenseState { HasExtendedData = true, Touch1 = new TouchPoint { Active = true, X = 5, Y = 5 } };
        var pad = new PadState { Source = ds, PassTouchpad = false };

        var r = new byte[DualShock4Pad.ReportSize];
        ushort ts = 0;
        uint? last = null;
        byte counter = 0;
        DualShock4Pad.BuildReport(pad, r, ref ts, ref last, ref counter);

        Assert.Equal(0x80, r[34]);
    }
}

public class GyroTests
{
    private static DualSenseState Sample(short yaw, uint timestamp) => new DualSenseState
    {
        HasExtendedData = true,
        GyroY = yaw,
        SensorTimestamp = timestamp,
        Buttons = DsButtons.L2,
    };

    [Fact]
    public void RightStickModeTurnsRightWhenYawIsNegative()
    {
        var gyro = new GyroAim();
        var settings = new GyroSettings { Mode = GyroMode.RightStick, Activation = GyroActivation.Always, SmoothingThreshold = 0 };
        var cal = GyroCalibration.Default;

        gyro.Update(Sample(0, 0), settings, cal); //establishes bias
        var (x, y) = gyro.Update(Sample(-1000, 12000), settings, cal);

        Assert.True(x > 0);
        Assert.Equal(0f, y, 3);
    }

    [Fact]
    public void InactiveUntilActivationButtonHeld()
    {
        var gyro = new GyroAim();
        var settings = new GyroSettings { Mode = GyroMode.RightStick, Activation = GyroActivation.WhileHeld, ActivationButton = DsButtons.R1 };
        var cal = GyroCalibration.Default;

        gyro.Update(Sample(0, 0), settings, cal);
        Assert.Equal((0f, 0f), gyro.Update(Sample(-1000, 12000), settings, cal));
    }

    [Fact]
    public void InvertFlipsDirection()
    {
        var gyro = new GyroAim();
        var settings = new GyroSettings { Mode = GyroMode.RightStick, Activation = GyroActivation.Always, InvertX = true, SmoothingThreshold = 0 };
        var cal = GyroCalibration.Default;

        gyro.Update(Sample(0, 0), settings, cal);
        var (x, _) = gyro.Update(Sample(-1000, 12000), settings, cal);

        Assert.True(x < 0);
    }

    [Fact]
    public void StickOutputHasAntiDeadzoneAndSaturates()
    {
        var g = new GyroSettings { StickFullSpeed = 100, StickAntiDeadzone = 0.2f };

        var (small, _) = GyroAim.ToStick(1f, 0f, g);
        Assert.InRange(small, 0.2f, 0.22f);

        var (big, _) = GyroAim.ToStick(1000f, 0f, g);
        Assert.Equal(1f, big, 3);
    }

    [Fact]
    public void SoftenOnlyAffectsSmallRates()
    {
        Assert.Equal(0.25f, GyroAim.Soften(0.5f, 1f), 3);
        Assert.Equal(5f, GyroAim.Soften(5f, 1f));
    }
}

public class ServiceHelperTests
{
    [Fact]
    public void BatteryColorGoesRedToGreen()
    {
        Assert.Equal(new RgbColor(255, 0, 0), ControllerService.BatteryColor(0));
        Assert.Equal(new RgbColor(255, 255, 0), ControllerService.BatteryColor(50));
        Assert.Equal(new RgbColor(0, 255, 0), ControllerService.BatteryColor(100));
    }

    [Fact]
    public void LowBatteryOnlyWhenDischarging()
    {
        var status = new ControllerStatus(true, "USB", true, 15, BatteryState.Discharging, "");
        Assert.True(status.IsLowBattery(20));
        Assert.False((status with { Battery = BatteryState.Charging }).IsLowBattery(20));
        Assert.False((status with { BatteryPercent = 50 }).IsLowBattery(20));
    }
}
