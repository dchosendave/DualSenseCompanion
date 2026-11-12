using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DualSenseCompanion.App;

public sealed record Conflict(string Title, string Detail);

//Finds setups that cause double input or fights over the controller
public static class ConflictDetector
{
    //Process name (without .exe) -> name shown to the user
    private static readonly (string Process, string Name)[] OtherControllerApps =
    {
        ("DS4Windows", "DS4Windows"),
        ("DSX", "DSX (DualSenseX)"),
        ("DualSenseX", "DualSenseX"),
        ("reWASD", "reWASD"),
        ("reWASDTray", "reWASD"),
        ("x360ce", "x360ce"),
        ("x360ce_x64", "x360ce"),
    };

    public static IReadOnlyList<Conflict> Detect(EmulationMode mode, bool hidingAvailable)
    {
        var running = RunningProcessNames();
        return Evaluate(mode, hidingAvailable, running, running.Contains("steam") ? ReadSteamPlayStationSupport() : null);
    }

    //Pure decision logic, separated for tests. steamPsSupport: null = unknown or Steam not running.
    public static IReadOnlyList<Conflict> Evaluate(EmulationMode mode, bool hidingAvailable, ISet<string> runningProcesses, bool? steamPsSupport)
    {
        var conflicts = new List<Conflict>();

        foreach (string name in OtherControllerApps.Where(a => runningProcesses.Contains(a.Process)).Select(a => a.Name).Distinct())
        {
            conflicts.Add(new Conflict(
                $"{name} is running",
                $"It also reads your controller, which can cause double input or fights over the lightbar and triggers. Close {name} while using DualSense Companion."));
        }

        if (mode == EmulationMode.DualShock4 && runningProcesses.Contains("steam") && steamPsSupport != false)
        {
            conflicts.Add(new Conflict(
                "Steam may double your input",
                "Steam's PlayStation controller support can take over the virtual DualShock 4, so games see two controllers. In Steam, open Settings > Controller and turn off PlayStation controller support, or pick Xbox here."));
        }

        if (mode != EmulationMode.Native && !hidingAvailable)
        {
            conflicts.Add(new Conflict(
                "Games may see two controllers",
                "HidHide isn't installed or active, so games see both your DualSense and the virtual controller. Reinstall HidHide and restart your PC."));
        }

        return conflicts;
    }

    private static HashSet<string> RunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch
            {
                //Process exited meanwhile
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }

    //Steam's "PlayStation Controller Support" setting: true = on, false = off, null = unknown
    private static bool? ReadSteamPlayStationSupport()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");

            if (key?.GetValue("SteamPath") is not string steamPath)
            {
                return null;
            }

            string config = Path.Combine(steamPath, "config", "config.vdf");
            return File.Exists(config) ? ParsePlayStationSupport(File.ReadAllText(config)) : null;
        }
        catch
        {
            return null;
        }
    }

    //"SteamController_PSSupport" "0" = off, "1" = for games without PlayStation support, "2" = on everywhere
    public static bool? ParsePlayStationSupport(string vdf)
    {
        var match = Regex.Match(vdf, "\"SteamController_PSSupport\"\\s+\"(\\d+)\"");
        return match.Success ? match.Groups[1].Value != "0" : null;
    }
}
