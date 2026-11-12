using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;

namespace DualSenseCompanion.Features;

//Every input a new controller should be checked for, and how to tell it was used
public sealed record ChecklistItem(string Group, string Name, Func<DualSenseState, float, bool> IsActive);

public static class ControllerChecklist
{
    private const float StickThreshold = 0.85f;
    private const byte FullTrigger = 245;
    private const float GyroThreshold = 120f; //degrees per second

    private static float X(byte raw) => InputMapper.NormalizeAxis(raw);
    private static float Y(byte raw) => -InputMapper.NormalizeAxis(raw);

    //The float argument is the fastest gyro rotation in degrees per second
    public static IReadOnlyList<ChecklistItem> Items { get; } = new List<ChecklistItem>
    {
        new("Face buttons", "Cross", (s, _) => s.IsPressed(DsButtons.Cross)),
        new("Face buttons", "Circle", (s, _) => s.IsPressed(DsButtons.Circle)),
        new("Face buttons", "Square", (s, _) => s.IsPressed(DsButtons.Square)),
        new("Face buttons", "Triangle", (s, _) => s.IsPressed(DsButtons.Triangle)),

        new("D-pad", "Up", (s, _) => s.IsPressed(DsButtons.DpadUp)),
        new("D-pad", "Down", (s, _) => s.IsPressed(DsButtons.DpadDown)),
        new("D-pad", "Left", (s, _) => s.IsPressed(DsButtons.DpadLeft)),
        new("D-pad", "Right", (s, _) => s.IsPressed(DsButtons.DpadRight)),

        new("Shoulders and triggers", "L1", (s, _) => s.IsPressed(DsButtons.L1)),
        new("Shoulders and triggers", "R1", (s, _) => s.IsPressed(DsButtons.R1)),
        new("Shoulders and triggers", "L2 fully pressed", (s, _) => s.L2 >= FullTrigger),
        new("Shoulders and triggers", "R2 fully pressed", (s, _) => s.R2 >= FullTrigger),

        new("Left stick", "Up", (s, _) => Y(s.LeftY) > StickThreshold),
        new("Left stick", "Down", (s, _) => Y(s.LeftY) < -StickThreshold),
        new("Left stick", "Left", (s, _) => X(s.LeftX) < -StickThreshold),
        new("Left stick", "Right", (s, _) => X(s.LeftX) > StickThreshold),
        new("Left stick", "Click (L3)", (s, _) => s.IsPressed(DsButtons.L3)),

        new("Right stick", "Up", (s, _) => Y(s.RightY) > StickThreshold),
        new("Right stick", "Down", (s, _) => Y(s.RightY) < -StickThreshold),
        new("Right stick", "Left", (s, _) => X(s.RightX) < -StickThreshold),
        new("Right stick", "Right", (s, _) => X(s.RightX) > StickThreshold),
        new("Right stick", "Click (R3)", (s, _) => s.IsPressed(DsButtons.R3)),

        new("Center", "Create", (s, _) => s.IsPressed(DsButtons.Create)),
        new("Center", "Options", (s, _) => s.IsPressed(DsButtons.Options)),
        new("Center", "PS button", (s, _) => s.IsPressed(DsButtons.PS)),
        new("Center", "Mute button", (s, _) => s.IsPressed(DsButtons.Mute)),

        new("Touchpad and motion", "Touch", (s, _) => s.Touch1.Active),
        new("Touchpad and motion", "Two fingers", (s, _) => s.Touch1.Active && s.Touch2.Active),
        new("Touchpad and motion", "Click", (s, _) => s.IsPressed(DsButtons.Touchpad)),
        new("Touchpad and motion", "Gyro (turn it quickly)", (_, gyro) => gyro >= GyroThreshold),
    };
}

public sealed record HealthResult(string Name, CheckResult Result, string Detail);

//Turns raw samples from the guided health check into plain-language results
public static class ControllerHealth
{
    public const int ReachSectors = 12;

    //Rest position: how far the stick sits from center when untouched, as a fraction of full travel
    public static HealthResult StickDrift(string stick, IReadOnlyList<(byte X, byte Y)> rest)
    {
        if (rest.Count == 0)
        {
            return new HealthResult($"{stick} drift", CheckResult.Skipped, "No data");
        }

        double x = rest.Average(p => InputMapper.NormalizeAxis(p.X));
        double y = rest.Average(p => InputMapper.NormalizeAxis(p.Y));
        double offset = Math.Sqrt(x * x + y * y);
        int percent = (int)Math.Round(offset * 100);

        if (offset < 0.05)
        {
            return new HealthResult($"{stick} drift", CheckResult.Pass, $"Rests {percent}% from center");
        }

        int suggested = Math.Min(40, percent + 5);
        return new HealthResult($"{stick} drift", CheckResult.Fail,
            $"Rests {percent}% from center, so it can move on its own in games. Set its deadzone to about {suggested}% on the Sticks page.");
    }

    //Noise: how much the reading wobbles while untouched, in raw steps (0-255)
    public static HealthResult StickJitter(string stick, IReadOnlyList<(byte X, byte Y)> rest)
    {
        if (rest.Count < 2)
        {
            return new HealthResult($"{stick} steadiness", CheckResult.Skipped, "No data");
        }

        double noise = Math.Max(StdDev(rest.Select(p => (double)p.X)), StdDev(rest.Select(p => (double)p.Y)));

        return noise <= 1.5
            ? new HealthResult($"{stick} steadiness", CheckResult.Pass, "Steady when untouched")
            : new HealthResult($"{stick} steadiness", CheckResult.Fail, $"Jitters by about {noise:0.0} steps when untouched; the stick sensor may be worn.");
    }

    //Reach: the stick should get close to the edge in every direction
    public static HealthResult StickReach(string stick, IReadOnlyList<(byte X, byte Y)> circling)
    {
        var maxPerSector = new double[ReachSectors];

        foreach (var (rawX, rawY) in circling)
        {
            double x = InputMapper.NormalizeAxis(rawX);
            double y = -InputMapper.NormalizeAxis(rawY);
            double magnitude = Math.Sqrt(x * x + y * y);

            if (magnitude < 0.3)
            {
                continue;
            }

            double angle = Math.Atan2(y, x);
            int sector = (int)((angle + Math.PI) / (2 * Math.PI) * ReachSectors) % ReachSectors;
            maxPerSector[sector] = Math.Max(maxPerSector[sector], magnitude);
        }

        double weakest = maxPerSector.Min();

        if (weakest == 0)
        {
            return new HealthResult($"{stick} range", CheckResult.Fail, "Didn't reach every direction. Run the check again and rotate the stick fully around its edge.");
        }

        int percent = (int)Math.Round(Math.Min(weakest, 1) * 100);

        return weakest >= 0.92
            ? new HealthResult($"{stick} range", CheckResult.Pass, $"Reaches {percent}% or more in every direction")
            : new HealthResult($"{stick} range", CheckResult.Fail, $"Only reaches {percent}% in some directions. Lower its outer deadzone on the Sticks page.");
    }

    public static HealthResult Trigger(string trigger, byte restMax, byte pressedMax)
    {
        if (restMax > 12)
        {
            return new HealthResult(trigger, CheckResult.Fail, $"Reads {restMax * 100 / 255}% when released. Add a trigger deadzone on the Sticks page.");
        }

        int percent = pressedMax * 100 / 255;

        return pressedMax >= 245
            ? new HealthResult(trigger, CheckResult.Pass, "Full travel, rests at zero")
            : new HealthResult(trigger, CheckResult.Fail, pressedMax == 0
                ? "Not pressed during the check. Run it again and press it all the way."
                : $"Only reaches {percent}% when fully pressed.");
    }

    //Gyro noise while lying still, in degrees per second
    public static HealthResult GyroNoise(IReadOnlyList<float> restRates)
    {
        if (restRates.Count < 2)
        {
            return new HealthResult("Gyro", CheckResult.Skipped, "No motion data");
        }

        double noise = StdDev(restRates.Select(r => (double)r));

        return noise < 1.0
            ? new HealthResult("Gyro", CheckResult.Pass, $"Steady when still ({noise:0.00}°/s noise)")
            : new HealthResult("Gyro", CheckResult.Fail, $"Noisy when still ({noise:0.0}°/s). Make sure it was lying still, then calibrate on the Gyro page.");
    }

    public static string Verdict(IEnumerable<HealthResult> results)
    {
        int failed = results.Count(r => r.Result == CheckResult.Fail);

        return failed switch
        {
            0 => "Healthy: everything is within normal range.",
            1 => "Mostly healthy: one thing needs attention.",
            _ => $"{failed} things need attention.",
        };
    }

    private static double StdDev(IEnumerable<double> values)
    {
        var list = values.ToList();

        if (list.Count < 2)
        {
            return 0;
        }

        double mean = list.Average();
        return Math.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1));
    }
}
