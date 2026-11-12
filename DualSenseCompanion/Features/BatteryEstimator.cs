using DualSenseCompanion.Device;

namespace DualSenseCompanion.Features;

//Estimates drain speed from the moments the battery level drops.
//The controller only reports 10% steps, so an estimate needs at least two drops.
public sealed class BatteryEstimator
{
    private readonly List<(double Time, int Percent)> _drops = new();
    private int? _lastPercent;

    public void Reset()
    {
        _drops.Clear();
        _lastPercent = null;
    }

    public void Add(double timeSeconds, int percent, BatteryState state)
    {
        if (state != BatteryState.Discharging)
        {
            //Charging or full: the old rate no longer applies
            Reset();
            return;
        }

        if (_lastPercent.HasValue && percent < _lastPercent.Value)
        {
            _drops.Add((timeSeconds, percent));
        }

        _lastPercent = percent;
    }

    //Percent per hour, or null until there's enough data
    public double? DrainPerHour
    {
        get
        {
            if (_drops.Count < 2)
            {
                return null;
            }

            var first = _drops[0];
            var last = _drops[^1];
            double hours = (last.Time - first.Time) / 3600.0;
            int used = first.Percent - last.Percent;

            return hours > 0 && used > 0 ? used / hours : null;
        }
    }

    public static TimeSpan? Remaining(int percent, double? drainPerHour)
    {
        if (drainPerHour is not > 0)
        {
            return null;
        }

        double hours = Math.Min(percent / drainPerHour.Value, 24);
        return TimeSpan.FromHours(hours);
    }

    public static string Describe(TimeSpan remaining) =>
        remaining.TotalMinutes < 60
            ? $"about {Math.Max(1, (int)remaining.TotalMinutes)} min left"
            : $"about {(int)remaining.TotalHours} h {remaining.Minutes:00} min left";
}
