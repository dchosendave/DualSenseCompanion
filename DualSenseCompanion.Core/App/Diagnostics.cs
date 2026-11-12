using DualSenseCompanion.Device;

namespace DualSenseCompanion.App;

public enum CheckResult
{
    Pass,
    Fail,
    Skipped,
}

public sealed record DiagnosticCheck(string Name, CheckResult Result, string Detail);

public sealed record DiagnosticReport(IReadOnlyList<DiagnosticCheck> Checks)
{
    public bool AllPassed => Checks.All(c => c.Result != CheckResult.Fail);
}

public static class Diagnostics
{
    //The controller confirms an output report was applied when its reported trigger state changes with the
    //effect and returns once the effect is turned off again
    public static CheckResult EvaluateTriggers(ulong baseline, ulong withEffect, ulong afterOff) =>
        withEffect != baseline && afterOff == baseline ? CheckResult.Pass : CheckResult.Fail;

    public static string Hex(ulong value) => BitConverter.ToString(BitConverter.GetBytes(value));

    //Typical rates: ~250 Hz over USB and Bluetooth. Anything far lower means a poor connection.
    public static CheckResult EvaluateInputRate(int reportsPerSecond) =>
        reportsPerSecond >= 100 ? CheckResult.Pass : CheckResult.Fail;
}
