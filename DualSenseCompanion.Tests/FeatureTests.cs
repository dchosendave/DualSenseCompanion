using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;
using DualSenseCompanion.Features;

namespace DualSenseCompanion.Tests;

public class BatteryEstimatorTests
{
    [Fact]
    public void NeedsTwoDropsBeforeEstimating()
    {
        var e = new BatteryEstimator();
        e.Add(0, 75, BatteryState.Discharging);
        Assert.Null(e.DrainPerHour);

        e.Add(1800, 65, BatteryState.Discharging);
        Assert.Null(e.DrainPerHour);

        e.Add(3600, 55, BatteryState.Discharging);
        Assert.Equal(20.0, e.DrainPerHour!.Value, 3); //10% per 30 min
    }

    [Fact]
    public void ChargingResetsTheEstimate()
    {
        var e = new BatteryEstimator();
        e.Add(0, 75, BatteryState.Discharging);
        e.Add(1800, 65, BatteryState.Discharging);
        e.Add(3600, 55, BatteryState.Discharging);
        e.Add(4000, 55, BatteryState.Charging);

        Assert.Null(e.DrainPerHour);
    }

    [Fact]
    public void RemainingTime()
    {
        var left = BatteryEstimator.Remaining(55, 20);
        Assert.Equal(TimeSpan.FromHours(2.75), left);
        Assert.Equal("about 2 h 45 min left", BatteryEstimator.Describe(left!.Value));
        Assert.Equal("about 30 min left", BatteryEstimator.Describe(TimeSpan.FromMinutes(30)));
        Assert.Null(BatteryEstimator.Remaining(55, null));
    }

    [Fact]
    public void StatusOnlyShowsTimeWhileDischarging()
    {
        var status = new ControllerStatus(true, "Bluetooth", true, 55, BatteryState.Discharging, "Xbox 360", DrainPerHour: 20);
        Assert.NotNull(status.BatteryRemaining);
        Assert.Null((status with { Battery = BatteryState.Charging }).BatteryRemaining);
    }
}

public class LightbarEffectTests
{
    [Fact]
    public void BreathingStaysInRange()
    {
        for (double t = 0; t < 4; t += 0.05)
        {
            Assert.InRange(LightbarEffects.Breathing(t, 1f), 0.15f, 1f);
        }
    }

    [Fact]
    public void RainbowHitsPrimaryColors()
    {
        Assert.Equal(new RgbColor(255, 0, 0), LightbarEffects.FromHue(0));
        Assert.Equal(new RgbColor(0, 255, 0), LightbarEffects.FromHue(1.0 / 3));
        Assert.Equal(new RgbColor(0, 0, 255), LightbarEffects.FromHue(2.0 / 3));
    }

    [Fact]
    public void AudioBrightnessNeverGoesDark()
    {
        Assert.Equal(0.1f, LightbarEffects.AudioBrightness(0), 3);
        Assert.Equal(1f, LightbarEffects.AudioBrightness(1), 3);
    }
}

public class ConflictDetectorTests
{
    private static HashSet<string> Running(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void CleanSetupHasNoWarnings()
    {
        Assert.Empty(ConflictDetector.Evaluate(EmulationMode.Xbox360, true, Running("explorer", "steam"), steamPsSupport: true));
    }

    [Fact]
    public void WarnsAboutOtherControllerApps()
    {
        var found = ConflictDetector.Evaluate(EmulationMode.Xbox360, true, Running("ds4windows"), null);
        Assert.Contains(found, c => c.Title.Contains("DS4Windows"));
    }

    [Fact]
    public void WarnsAboutSteamOnlyInDualShock4Mode()
    {
        Assert.Contains(ConflictDetector.Evaluate(EmulationMode.DualShock4, true, Running("steam"), true), c => c.Title.Contains("Steam"));
        Assert.Contains(ConflictDetector.Evaluate(EmulationMode.DualShock4, true, Running("steam"), null), c => c.Title.Contains("Steam"));
        Assert.Empty(ConflictDetector.Evaluate(EmulationMode.DualShock4, true, Running("steam"), false));
        Assert.Empty(ConflictDetector.Evaluate(EmulationMode.DualShock4, true, Running("explorer"), true));
    }

    [Fact]
    public void WarnsWhenHidHideMissingExceptInNativeMode()
    {
        Assert.Contains(ConflictDetector.Evaluate(EmulationMode.Xbox360, false, Running(), null), c => c.Title.Contains("two controllers"));
        Assert.Empty(ConflictDetector.Evaluate(EmulationMode.Native, false, Running(), null));
    }

    [Theory]
    [InlineData("\"SteamController_PSSupport\"\t\t\"2\"", true)]
    [InlineData("\"SteamController_PSSupport\" \"1\"", true)]
    [InlineData("\"SteamController_PSSupport\"\t\"0\"", false)]
    [InlineData("\"SomethingElse\" \"2\"", null)]
    public void ParsesSteamPlayStationSetting(string vdf, bool? expected)
    {
        Assert.Equal(expected, ConflictDetector.ParsePlayStationSupport(vdf));
    }
}

public class ControllerHealthTests
{
    private static List<(byte, byte)> Repeat(byte x, byte y, int count) => Enumerable.Repeat((x, y), count).ToList();

    [Fact]
    public void CenteredStickPasses()
    {
        Assert.Equal(CheckResult.Pass, ControllerHealth.StickDrift("Left stick", Repeat(128, 129, 50)).Result);
        Assert.Equal(CheckResult.Pass, ControllerHealth.StickJitter("Left stick", Repeat(128, 129, 50)).Result);
    }

    [Fact]
    public void DriftingStickFailsWithDeadzoneAdvice()
    {
        var result = ControllerHealth.StickDrift("Left stick", Repeat(148, 128, 50)); //~16% right
        Assert.Equal(CheckResult.Fail, result.Result);
        Assert.Contains("deadzone", result.Detail);
    }

    [Fact]
    public void JitteryStickFails()
    {
        var samples = Enumerable.Range(0, 50).Select(i => ((byte)(i % 2 == 0 ? 124 : 132), (byte)128)).ToList();
        Assert.Equal(CheckResult.Fail, ControllerHealth.StickJitter("Left stick", samples).Result);
    }

    [Fact]
    public void FullCirclePassesReach()
    {
        var circle = Enumerable.Range(0, 360).Select(deg =>
        {
            double a = deg * Math.PI / 180;
            return ((byte)Math.Round(128 + 127 * Math.Cos(a)), (byte)Math.Round(128 - 127 * Math.Sin(a)));
        }).ToList();

        Assert.Equal(CheckResult.Pass, ControllerHealth.StickReach("Left stick", circle).Result);
    }

    [Fact]
    public void HalfCircleFailsReach()
    {
        var half = Enumerable.Range(0, 180).Select(deg =>
        {
            double a = deg * Math.PI / 180;
            return ((byte)Math.Round(128 + 127 * Math.Cos(a)), (byte)Math.Round(128 - 127 * Math.Sin(a)));
        }).ToList();

        Assert.Equal(CheckResult.Fail, ControllerHealth.StickReach("Left stick", half).Result);
    }

    [Theory]
    [InlineData(0, 255, CheckResult.Pass)]
    [InlineData(0, 200, CheckResult.Fail)]
    [InlineData(30, 255, CheckResult.Fail)]
    [InlineData(0, 0, CheckResult.Fail)]
    public void Triggers(byte rest, byte pressed, CheckResult expected)
    {
        Assert.Equal(expected, ControllerHealth.Trigger("L2", rest, pressed).Result);
    }

    [Fact]
    public void GyroNoise()
    {
        Assert.Equal(CheckResult.Pass, ControllerHealth.GyroNoise(new List<float> { 0.1f, -0.2f, 0.15f, 0f }).Result);
        Assert.Equal(CheckResult.Fail, ControllerHealth.GyroNoise(new List<float> { 5f, -6f, 4f, -5f }).Result);
    }

    [Fact]
    public void Verdicts()
    {
        var pass = new HealthResult("a", CheckResult.Pass, "");
        var fail = new HealthResult("b", CheckResult.Fail, "");
        Assert.StartsWith("Healthy", ControllerHealth.Verdict(new[] { pass, pass }));
        Assert.StartsWith("Mostly healthy", ControllerHealth.Verdict(new[] { pass, fail }));
        Assert.StartsWith("2 things", ControllerHealth.Verdict(new[] { fail, fail }));
    }
}

public class ChecklistTests
{
    private static ChecklistItem Item(string group, string name) =>
        ControllerChecklist.Items.Single(i => i.Group == group && i.Name == name);

    [Fact]
    public void ButtonsAndDirectionsDetect()
    {
        var pressed = new DualSenseState { Buttons = DsButtons.Cross | DsButtons.DpadLeft, LeftX = 255, RightY = 0, L2 = 255 };

        Assert.True(Item("Face buttons", "Cross").IsActive(pressed, 0));
        Assert.False(Item("Face buttons", "Circle").IsActive(pressed, 0));
        Assert.True(Item("D-pad", "Left").IsActive(pressed, 0));
        Assert.True(Item("Left stick", "Right").IsActive(pressed, 0));
        Assert.True(Item("Right stick", "Up").IsActive(pressed, 0));
        Assert.True(Item("Shoulders and triggers", "L2 fully pressed").IsActive(pressed, 0));
        Assert.False(Item("Shoulders and triggers", "R2 fully pressed").IsActive(pressed, 0));
    }

    [Fact]
    public void GyroNeedsAQuickTurn()
    {
        var item = Item("Touchpad and motion", "Gyro (turn it quickly)");
        Assert.False(item.IsActive(new DualSenseState(), 30));
        Assert.True(item.IsActive(new DualSenseState(), 200));
    }

    [Fact]
    public void EveryItemHasAUniqueName()
    {
        Assert.Equal(ControllerChecklist.Items.Count, ControllerChecklist.Items.Select(i => i.Group + i.Name).Distinct().Count());
    }
}

public class ControllerInfoTests
{
    [Fact]
    public void BluetoothAddressIsReversedFromTheReport()
    {
        var report = new byte[20];
        report[0] = 0x09;
        byte[] lowFirst = { 0xB2, 0xDA, 0x75, 0x46, 0xC7, 0xBC };
        lowFirst.CopyTo(report, 1);

        Assert.Equal("BC:C7:46:75:DA:B2", ControllerInfo.ParseBluetoothAddress(report));
    }

    [Fact]
    public void EmptyOrWrongReportsGiveNoAddress()
    {
        Assert.Null(ControllerInfo.ParseBluetoothAddress(new byte[20] { 0x09, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Null(ControllerInfo.ParseBluetoothAddress(new byte[20]));
    }

    [Fact]
    public void FirmwareReport()
    {
        var report = new byte[64];
        report[0] = 0x20;
        System.Text.Encoding.ASCII.GetBytes("Jun 14 2023").CopyTo(report, 1);
        System.Text.Encoding.ASCII.GetBytes("10:30:00").CopyTo(report, 12);
        BitConverter.GetBytes(0x00000414u).CopyTo(report, 24); //hardware
        BitConverter.GetBytes(0x0110002Au).CopyTo(report, 28); //firmware
        BitConverter.GetBytes((ushort)0x0430).CopyTo(report, 44); //update version

        var (update, firmware, hardware, built) = ControllerInfo.ParseFirmware(report);

        Assert.Equal("04.30", update);
        Assert.Equal("0110002A", firmware);
        Assert.Equal("00000414", hardware);
        Assert.Equal("Jun 14 2023 10:30:00", built);
    }

    [Fact]
    public void WrongFirmwareReportIsIgnored()
    {
        Assert.Equal((null, null, null, null), ControllerInfo.ParseFirmware(new byte[64]));
    }
}
