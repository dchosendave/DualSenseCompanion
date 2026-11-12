using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Nefarius.Drivers.HidHide;

namespace DualSenseCompanion.App;

public static class CloakManager
{
    private static readonly HidHideControlService _hidHide = new HidHideControlService();
    private static readonly string _exePath = Environment.ProcessPath!;

    private static readonly object _lock = new object();
    private static readonly HashSet<string> _hiddenIds = new HashSet<string>();
    private static readonly List<PosixSignalRegistration> _signalRegistrations = new List<PosixSignalRegistration>();
    private static bool _wasActive;
    private static bool _initialized;
    private static bool _cleanedUp;
    private static readonly HiddenDeviceStore _store = HiddenDeviceStore.Default;

    //Whitelists this app and registers cleanup for every way the process can end
    public static void Initialize()
    {
        if (!_hidHide.IsInstalled)
        {
            Console.WriteLine("HidHide is not installed. Controller will not be hidden.");
            return;
        }

        _wasActive = _hidHide.IsActive;
        RestoreAfterUncleanExit();
        WhitelistCurrentApp();
        _initialized = true;

        //Normal exit
        AppDomain.CurrentDomain.ProcessExit += (s, e) => UnhidePS5Controller();

        //Crash
        AppDomain.CurrentDomain.UnhandledException += (s, e) => UnhidePS5Controller();

        //Ctrl+C, Ctrl+Break, closing the console window, logoff/shutdown
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
        {
            _signalRegistrations.Add(PosixSignalRegistration.Create(signal, ctx => UnhidePS5Controller()));
        }
    }

    //A previous run that was killed or crashed hard couldn't unhide the controller; do it now
    private static void RestoreAfterUncleanExit()
    {
        var leftover = _store.Load();

        if (leftover == null)
        {
            return;
        }

        try
        {
            foreach (string instanceId in leftover.InstanceIds)
            {
                if (_hidHide.BlockedInstanceIds.Contains(instanceId))
                {
                    _hidHide.RemoveBlockedInstanceId(instanceId);
                }
            }

            //Restore HidHide to how it was before that run, not how that run left it
            _hidHide.IsActive = leftover.HidHideWasActive;
            _wasActive = leftover.HidHideWasActive;
            _store.Clear();
            Console.WriteLine($"Restored {leftover.InstanceIds.Count} controller(s) left hidden by a previous run that didn't exit cleanly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not restore controllers hidden by a previous run: {ex.Message}");
        }
    }

    public static void HidePS5Controller()
    {
        if (!_initialized)
        {
            return;
        }

        string? instanceId = FindPS5ControllerInstanceId();

        if (instanceId == null)
        {
            Console.WriteLine("Controller not found in the device list; it will not be hidden.");
            return;
        }

        lock (_lock)
        {
            if (_cleanedUp)
            {
                return;
            }

            if (!_hidHide.BlockedInstanceIds.Contains(instanceId))
            {
                _hidHide.AddBlockedInstanceId(instanceId);
                Console.WriteLine($"Added PS5 Controller ({instanceId}) to HidHide block list.");
            }

            //Tracked even if it was already blocked (e.g. left over from a crash) so it gets unhidden on exit
            _hiddenIds.Add(instanceId);
            _store.Save(new HiddenDeviceRecord(_wasActive, _hiddenIds.ToList()));

            _hidHide.IsActive = true;
            Console.WriteLine("PS5 Controller is now hidden!");
        }
    }

    //Makes the controller visible to games again while the app keeps running (Native DualSense mode)
    public static void ShowPS5Controller()
    {
        lock (_lock)
        {
            if (!_initialized || _cleanedUp || _hiddenIds.Count == 0)
            {
                return;
            }

            try
            {
                foreach (string instanceId in _hiddenIds)
                {
                    _hidHide.RemoveBlockedInstanceId(instanceId);
                }

                _hiddenIds.Clear();
                _store.Clear();
                Console.WriteLine("PS5 Controller is visible to games (native mode).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to unhide controller: {ex.Message}");
            }
        }
    }

    //Safe to call more than once and from any thread
    public static void UnhidePS5Controller()
    {
        lock (_lock)
        {
            if (!_initialized || _cleanedUp)
            {
                return;
            }

            _cleanedUp = true;

            try
            {
                foreach (string instanceId in _hiddenIds)
                {
                    _hidHide.RemoveBlockedInstanceId(instanceId);
                }

                if (_hiddenIds.Count > 0)
                {
                    Console.WriteLine("PS5 Controller removed from HidHide block list.");
                }

                _hidHide.IsActive = _wasActive;
                _store.Clear();

                if (_hidHide.ApplicationPaths.Contains(_exePath))
                {
                    _hidHide.RemoveApplicationPath(_exePath);
                    Console.WriteLine("Removed application from HidHide whitelist.");
                }

                Console.WriteLine("PS5 Controller is now visible!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to restore HidHide settings: {ex.Message}");
            }
        }
    }

    //Matches USB ("VID_054C&PID_0CE6") and Bluetooth ("_VID&0002054C_PID&0CE6") instance IDs,
    //for both the DualSense and the DualSense Edge (PID 0DF2)
    private static readonly string[] InstanceIdPatterns =
    {
        "VID_054C&PID_0CE6", "_VID&0002054C_PID&0CE6",
        "VID_054C&PID_0DF2", "_VID&0002054C_PID&0DF2",
    };

    //Waits a few seconds for the controller to show up in the device list
    public static string? FindPS5ControllerInstanceId(int timeoutSeconds = 5)
    {
        for (int attempt = 0; attempt < timeoutSeconds; attempt++)
        {
            foreach (var device in GetAllHIDDevices())
            {
                if (InstanceIdPatterns.Any(p => device.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    return device;
                }
            }

            Thread.Sleep(1000);
        }

        return null;
    }

    public static List<string> GetAllHIDDevices()
    {
        List<string> deviceList = new List<string>();

        try
        {
            using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE 'HID%'"))
            {
                foreach (ManagementObject device in searcher.Get())
                {
                    string? deviceId = device["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        deviceList.Add(deviceId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving HID devices: {ex.Message}");
        }

        return deviceList;
    }

    private static void WhitelistCurrentApp()
    {
        if (!_hidHide.ApplicationPaths.Contains(_exePath))
        {
            _hidHide.AddApplicationPath(_exePath);
            Console.WriteLine($"Added {Path.GetFileName(_exePath)} to HidHide whitelist.");
        }
        else
        {
            Console.WriteLine("Application is already whitelisted.");
        }
    }
}
