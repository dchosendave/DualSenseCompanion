using System.IO;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.Tests;

public class OutputPolicyTests
{
    [Theory]
    [InlineData(EmulationMode.Xbox360)]
    [InlineData(EmulationMode.DualShock4)]
    public void EmulationModesAlwaysSend(EmulationMode mode)
    {
        Assert.True(OutputPolicy.ShouldSend(mode, nowMs: 10_000, userOutputUntilMs: 0, selfTestRunning: false));
    }

    [Fact]
    public void NativeModeOnlySendsRightAfterUserChange()
    {
        Assert.True(OutputPolicy.ShouldSend(EmulationMode.Native, nowMs: 1_000, userOutputUntilMs: 1_500, selfTestRunning: false));
        Assert.False(OutputPolicy.ShouldSend(EmulationMode.Native, nowMs: 1_500, userOutputUntilMs: 1_500, selfTestRunning: false));
        Assert.False(OutputPolicy.ShouldSend(EmulationMode.Native, nowMs: 9_000, userOutputUntilMs: 1_500, selfTestRunning: false));
    }

    [Fact]
    public void SelfTestSendsEvenInNativeMode()
    {
        Assert.True(OutputPolicy.ShouldSend(EmulationMode.Native, nowMs: 9_000, userOutputUntilMs: 0, selfTestRunning: true));
    }
}

public class DiagnosticsTests
{
    [Fact]
    public void TriggersPassWhenStateChangesAndReturns()
    {
        Assert.Equal(CheckResult.Pass, Diagnostics.EvaluateTriggers(baseline: 0x10, withEffect: 0x2A10, afterOff: 0x10));
    }

    [Fact]
    public void TriggersFailWhenControllerIgnoresOutput()
    {
        Assert.Equal(CheckResult.Fail, Diagnostics.EvaluateTriggers(0x10, 0x10, 0x10));
    }

    [Fact]
    public void TriggersFailWhenEffectStaysOn()
    {
        //The "Off didn't really turn off" bug
        Assert.Equal(CheckResult.Fail, Diagnostics.EvaluateTriggers(0x10, 0x2A10, 0x2A10));
    }

    [Theory]
    [InlineData(250, CheckResult.Pass)]
    [InlineData(100, CheckResult.Pass)]
    [InlineData(20, CheckResult.Fail)]
    public void InputRate(int rate, CheckResult expected)
    {
        Assert.Equal(expected, Diagnostics.EvaluateInputRate(rate));
    }

    [Fact]
    public void ParserReadsTriggerFeedbackBytes()
    {
        var report = new byte[64];
        report[0] = 0x01;
        for (int i = 0; i < 8; i++)
        {
            report[1 + 40 + i] = (byte)(i + 1);
        }

        var state = new DualSenseState();
        DualSenseParser.TryParse(report, isBluetooth: false, state);

        Assert.Equal(0x0807060504030201UL, state.TriggerFeedback);
    }

    [Fact]
    public void UsbReportIs48Bytes()
    {
        var r = DualSenseOutputBuilder.Build(new DualSenseOutputState(), bluetooth: false, lightbarSetup: false);
        Assert.Equal(48, r.Length);
        Assert.Equal(0x02, r[0]);
    }
}

public class HiddenDeviceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dsc-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "hidden-devices.json");

    [Fact]
    public void NothingSavedMeansNothingToRestore()
    {
        Assert.Null(new HiddenDeviceStore(FilePath).Load());
    }

    [Fact]
    public void SavedDevicesSurviveARestart()
    {
        new HiddenDeviceStore(FilePath).Save(new HiddenDeviceRecord(false, new List<string> { @"HID\VID_054C&PID_0CE6\1" }));

        //A fresh instance, as after the app was killed and started again
        var loaded = new HiddenDeviceStore(FilePath).Load();

        Assert.NotNull(loaded);
        Assert.False(loaded!.HidHideWasActive);
        Assert.Equal(@"HID\VID_054C&PID_0CE6\1", Assert.Single(loaded.InstanceIds));
    }

    [Fact]
    public void ClearAfterCleanExit()
    {
        var store = new HiddenDeviceStore(FilePath);
        store.Save(new HiddenDeviceRecord(true, new List<string> { "a" }));
        store.Clear();

        Assert.Null(store.Load());
    }

    [Fact]
    public void EmptyOrCorruptFileIsIgnored()
    {
        Directory.CreateDirectory(_dir);

        File.WriteAllText(FilePath, "{ not json");
        Assert.Null(new HiddenDeviceStore(FilePath).Load());

        new HiddenDeviceStore(FilePath).Save(new HiddenDeviceRecord(true, new List<string>()));
        Assert.Null(new HiddenDeviceStore(FilePath).Load());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            //Best effort
        }
    }
}

public class SettingsTests
{
    [Fact]
    public void NativeModeRoundTripsThroughJson()
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var settings = new Settings { Emulation = EmulationMode.Native };

        var loaded = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings, options), options)!;

        Assert.Equal(EmulationMode.Native, loaded.Emulation);
    }

    [Fact]
    public void NormalizeClampsHandEditedValues()
    {
        var settings = new Settings { PlayerNumber = 99, RumbleStrength = -5 };
        settings.LeftStick.Deadzone = 3f;
        settings.Normalize();

        Assert.Equal(5, settings.PlayerNumber);
        Assert.Equal(0, settings.RumbleStrength);
        Assert.Equal(0.9f, settings.LeftStick.Deadzone);
    }
}
