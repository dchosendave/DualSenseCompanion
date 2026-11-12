using System.Diagnostics;
using DualSenseCompanion.Device;
using DualSenseCompanion.Emulation;
using DualSenseCompanion.Features;
using Nefarius.ViGEm.Client;

namespace DualSenseCompanion.App;

public sealed record ControllerStatus(
    bool Connected,
    string Connection,
    bool HasBattery,
    int BatteryPercent,
    BatteryState Battery,
    string Emulation,
    double? DrainPerHour = null)
{
    public TimeSpan? BatteryRemaining =>
        Connected && HasBattery && Battery == BatteryState.Discharging
            ? BatteryEstimator.Remaining(BatteryPercent, DrainPerHour)
            : null;

    public bool IsLowBattery(int threshold) =>
        Connected && HasBattery && Battery == BatteryState.Discharging && BatteryPercent <= threshold;

    public string Describe()
    {
        if (!Connected)
        {
            return "Waiting for controller...";
        }

        string text = $"DualSense ({Connection})";

        if (HasBattery)
        {
            string charge = Battery switch
            {
                BatteryState.Charging => ", charging",
                BatteryState.Full => ", full",
                BatteryState.Error => ", battery error",
                _ => "",
            };
            text += $" - {BatteryPercent}%{charge}";

            if (BatteryRemaining is { } remaining)
            {
                text += $", {BatteryEstimator.Describe(remaining)}";
            }
        }

        return text;
    }
}

//Everything the UI shows live, captured at one moment
public sealed record LiveSnapshot(
    ControllerStatus Status,
    DualSenseState? Input,
    PadState? Output,
    DualSenseOutputState Feedback,
    float GyroPitch,
    float GyroYaw,
    float GyroRoll,
    bool MicMuted,
    IReadOnlyList<Conflict>? Conflicts = null,
    ControllerInfo? Info = null);

//When the output thread may write to the controller
public static class OutputPolicy
{
    //Native mode leaves outputs to the game, except right after the user changed something here or during a self-test
    public static bool ShouldSend(EmulationMode mode, long nowMs, long userOutputUntilMs, bool selfTestRunning) =>
        selfTestRunning || mode != EmulationMode.Native || nowMs < userOutputUntilMs;
}

//Owns the controller connection, the virtual pad and every feature that runs per input report
public sealed class ControllerService : IDisposable
{
    private const int OutputIntervalMs = 50;
    private const int AnimatedOutputIntervalMs = 33;
    private const int MicPollIntervalMs = 1000;

    private volatile Settings _settings;
    private readonly ViGEmClient _client;
    private readonly object _padLock = new object();
    private readonly GyroAim _gyro = new GyroAim();
    private readonly TouchpadMouse _touchpad = new TouchpadMouse();
    private readonly AutoResetEvent _outputSignal = new AutoResetEvent(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IVirtualPad? _pad;
    private EmulationMode _padMode;
    private volatile DualSenseDevice? _device;
    private volatile bool _stopping;
    private Thread? _inputThread;
    private Thread? _outputThread;

    private volatile byte _rumbleLarge;
    private volatile byte _rumbleSmall;
    private RgbColor? _gameLightbar;
    private volatile bool _micMuted;
    private bool _lowBatteryNotified;
    private readonly BatteryEstimator _batteryEstimator = new BatteryEstimator();
    private readonly AudioLevelMeter _audioMeter = new AudioLevelMeter();

    //Other controller apps, Steam Input, missing HidHide; rechecked every few seconds
    private volatile IReadOnlyList<Conflict> _conflicts = Array.Empty<Conflict>();
    private readonly HashSet<string> _notifiedConflicts = new HashSet<string>();
    private Timer? _conflictTimer;
    private volatile float _audioLevel;
    private long _testRumbleUntilMs;

    //In native mode the game owns the outputs; the app only sends for a moment after the user changes something
    private long _userOutputUntilMs;

    //Set while the self-test drives the outputs directly
    private volatile DualSenseOutputState? _outputOverride;
    private volatile bool _selfTestRunning;

    private int _reportCount;
    private long _rateWindowStartMs;
    public int InputRate { get; private set; }
    private volatile DualSenseState? _latestInput;
    private volatile PadState? _latestOutput;

    private readonly object _statusLock = new object();
    private ControllerStatus _status;

    public event Action<ControllerStatus>? StatusChanged;
    public event Action<string>? Notification;

    public ControllerStatus Status
    {
        get { lock (_statusLock) return _status; }
    }

    public Settings Settings => _settings;

    public LiveSnapshot GetSnapshot() => new LiveSnapshot(
        Status,
        _latestInput,
        _latestOutput,
        ComputeOutput(),
        _gyro.LastPitch,
        _gyro.LastYaw,
        _gyro.LastRoll,
        _micMuted,
        _conflicts,
        _device?.Info);

    //Short rumble so the strength setting can be felt
    public void TestRumble()
    {
        _testRumbleUntilMs = _clock.ElapsedMilliseconds + 600;
        RequestOutput(1500);
    }

    //Call when the user changes an output setting (lightbar, triggers...) so it reaches the controller right away,
    //including in native mode
    public void RequestOutput(int forMs = 1000)
    {
        _userOutputUntilMs = _clock.ElapsedMilliseconds + forMs;
        _outputSignal.Set();
    }

    public ControllerService(Settings settings)
    {
        _settings = settings;
        _client = new ViGEmClient();
        _status = new ControllerStatus(false, "", false, 0, BatteryState.Unknown, "");
        _gyro.Message += message =>
        {
            Console.WriteLine(message);
            Notification?.Invoke(message);
        };

        CreatePad();
    }

    public void Start()
    {
        _micMuted = MicMute.IsMuted() ?? false;

        _inputThread = new Thread(InputLoop) { IsBackground = true, Name = "DualSense input" };
        _outputThread = new Thread(OutputLoop) { IsBackground = true, Name = "DualSense output" };
        _inputThread.Start();
        _outputThread.Start();
        _conflictTimer = new Timer(_ => CheckConflicts(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
    }

    public IReadOnlyList<Conflict> Conflicts => _conflicts;

    private void CheckConflicts()
    {
        if (_stopping)
        {
            return;
        }

        try
        {
            var found = ConflictDetector.Detect(_padMode, CloakManager.IsHidingAvailable);
            _conflicts = found;

            foreach (var conflict in found)
            {
                //Tell the user once per kind of problem, not every 10 seconds
                if (_notifiedConflicts.Add(conflict.Title))
                {
                    Console.WriteLine($"Warning: {conflict.Title}. {conflict.Detail}");
                    Notification?.Invoke($"{conflict.Title}. Open the app for details.");
                }
            }

            //Once a problem is gone, warn again if it comes back
            _notifiedConflicts.IntersectWith(found.Select(c => c.Title));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Conflict check failed: {ex.Message}");
        }
    }

    //Call after changing Settings
    public void ApplySettings()
    {
        _settings.Normalize();
        _settings.Save();
        _userOutputUntilMs = _clock.ElapsedMilliseconds + 1000;

        bool needsPad = _settings.Emulation != EmulationMode.Native;

        if (_settings.Emulation != _padMode || (needsPad && _pad == null))
        {
            try
            {
                CreatePad();
            }
            catch (Exception ex)
            {
                //Leave _padMode unchanged so the next settings change retries
                Console.WriteLine($"Could not switch the virtual controller: {ex}");
                Notification?.Invoke($"Could not create the virtual controller: {ex.Message}");
            }
        }

        _outputSignal.Set();

        //Mode changes affect which warnings apply
        _conflictTimer?.Change(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10));
    }

    public void ReplaceSettings(Settings settings)
    {
        _settings = settings;
        ApplySettings();
    }

    public void RecalibrateGyro()
    {
        Console.WriteLine("Calibrating gyro, keep the controller still...");
        _gyro.StartCalibration();
    }

    private void CreatePad()
    {
        lock (_padLock)
        {
            _pad?.Dispose();
            _pad = null;
            _gameLightbar = null;
            _rumbleLarge = 0;
            _rumbleSmall = 0;

            if (_settings.Emulation == EmulationMode.Native)
            {
                _padMode = EmulationMode.Native;
                Console.WriteLine("Native DualSense mode: no virtual controller, games use the DualSense directly.");
            }
            else
            {
                CreateVirtualPad();
            }
        }

        UpdateStatus(s => s with { Emulation = EmulationName(_padMode) });
        UpdateHiding();
    }

    public static string EmulationName(EmulationMode mode) => mode switch
    {
        EmulationMode.DualShock4 => "DualShock 4",
        EmulationMode.Native => "native DualSense",
        _ => "Xbox 360",
    };

    //Hidden while emulating so games don't see two controllers; visible in native mode
    private void UpdateHiding()
    {
        if (_device == null)
        {
            return;
        }

        bool native = _padMode == EmulationMode.Native;

        //HidHide and the device lookup can take a moment; keep the UI responsive
        Task.Run(() =>
        {
            if (native)
            {
                CloakManager.ShowPS5Controller();
            }
            else
            {
                CloakManager.HidePS5Controller();
            }
        });
    }

    private void CreateVirtualPad()
    {
        IVirtualPad pad = ConnectPadWithRetry(_settings.Emulation);

        pad.RumbleReceived += (large, small) =>
        {
            _rumbleLarge = large;
            _rumbleSmall = small;
            _outputSignal.Set();
        };

        pad.LightbarReceived += color =>
        {
            _gameLightbar = color;
            _outputSignal.Set();
        };

        _pad = pad;
        _padMode = _settings.Emulation;
        Console.WriteLine($"Virtual {pad.Name} controller created.");
    }

    //The ViGEm driver can briefly refuse new controllers, e.g. right after boot or a driver install
    private IVirtualPad ConnectPadWithRetry(EmulationMode mode)
    {
        const int attempts = 5;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return mode == EmulationMode.DualShock4
                    ? new DualShock4Pad(_client)
                    : new Xbox360Pad(_client);
            }
            catch (Exception ex) when (attempt < attempts)
            {
                Console.WriteLine($"Virtual controller connect failed (attempt {attempt}/{attempts}), retrying: {ex.Message}");
                Thread.Sleep(attempt * 1000);
            }
        }
    }

    private void InputLoop()
    {
        while (!_stopping)
        {
            DualSenseDevice? device = WaitForDevice();

            if (device == null)
            {
                return;
            }

            if (_padMode != EmulationMode.Native)
            {
                CloakManager.HidePS5Controller();
            }

            try
            {
                device.Open();
                _device = device;
                _gyro.ResetConnection();
                _batteryEstimator.Reset();
                Console.WriteLine($"DualSense{(device.IsEdge ? " Edge" : "")} connected ({device.ConnectionName}).");
                UpdateStatus(s => s with { Connected = true, Connection = device.ConnectionName, HasBattery = false });
                _outputSignal.Set();

                ReadReports(device);
            }
            catch (Exception ex) when (!_stopping)
            {
                Console.WriteLine($"Controller disconnected: {ex.Message}");
            }
            catch
            {
                //Stopping
            }
            finally
            {
                _device = null;
                _latestInput = null;
                _latestOutput = null;
                device.Dispose();
                _touchpad.ReleaseButtons();
                _rumbleLarge = 0;
                _rumbleSmall = 0;

                lock (_padLock)
                {
                    //Release everything so nothing stays "held" in the game
                    try
                    {
                        _pad?.Reset();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reset virtual controller: {ex.Message}");
                    }
                }

                UpdateStatus(s => s with { Connected = false, HasBattery = false });
            }

            if (!_stopping)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private DualSenseDevice? WaitForDevice()
    {
        Console.WriteLine("Looking for controller...");

        while (!_stopping)
        {
            var device = DualSenseDevice.TryFind();

            if (device != null)
            {
                return device;
            }

            Thread.Sleep(1000);
        }

        return null;
    }

    private void ReadReports(DualSenseDevice device)
    {
        var buffer = new byte[device.InputReportLength];
        var state = new DualSenseState();
        DsButtons previous = DsButtons.None;

        while (!_stopping)
        {
            int read = device.Read(buffer);

            if (read <= 0 || !DualSenseParser.TryParse(buffer.AsSpan(0, read), device.IsBluetooth, state))
            {
                continue;
            }

            HandleReport(device, state, previous);
            previous = state.Buttons;
            CountReport();
        }
    }

    private void CountReport()
    {
        _reportCount++;
        long now = _clock.ElapsedMilliseconds;

        if (now - _rateWindowStartMs >= 1000)
        {
            InputRate = (int)(_reportCount * 1000 / Math.Max(1, now - _rateWindowStartMs));
            _reportCount = 0;
            _rateWindowStartMs = now;
        }
    }

    //Checks input and output end to end. Flashes the lightbar and pulses rumble so the user can also see and feel it.
    public async Task<DiagnosticReport> RunSelfTestAsync()
    {
        var checks = new List<DiagnosticCheck>();
        var status = Status;

        if (!status.Connected)
        {
            checks.Add(new DiagnosticCheck("Controller connected", CheckResult.Fail, "No DualSense found. Plug it in or pair it, then run the test again."));
            return new DiagnosticReport(checks);
        }

        checks.Add(new DiagnosticCheck("Controller connected", CheckResult.Pass, $"Over {status.Connection}"));

        //Let the rate counter settle if the controller just connected
        await Task.Delay(1100);
        int rate = InputRate;
        checks.Add(new DiagnosticCheck("Input reports", Diagnostics.EvaluateInputRate(rate), $"{rate} per second"));

        var input = _latestInput;
        bool extended = input?.HasExtendedData == true;
        checks.Add(new DiagnosticCheck("Motion, touchpad and battery data", extended ? CheckResult.Pass : CheckResult.Fail,
            extended ? "Receiving full reports" : "Only basic reports; gyro, touchpad and battery are unavailable"));

        if (!extended)
        {
            checks.Add(new DiagnosticCheck("Controller accepts output", CheckResult.Skipped, "Needs full reports"));
            return new DiagnosticReport(checks);
        }

        _selfTestRunning = true;

        try
        {
            ulong Read() => _latestInput?.TriggerFeedback ?? 0;

            _outputOverride = new DualSenseOutputState { LightbarBlue = 255 };
            _outputSignal.Set();
            await Task.Delay(500);
            ulong baseline = Read();

            //Red lightbar, rumble, and resistance on both triggers
            _outputOverride = new DualSenseOutputState
            {
                LightbarRed = 255,
                LightbarBlue = 0,
                RumbleLeft = 160,
                RumbleRight = 160,
                PlayerLeds = 0x1F,
                LeftTrigger = TriggerPreset.Hard,
                RightTrigger = TriggerPreset.Weapon,
            };
            _outputSignal.Set();
            await Task.Delay(700);
            ulong withEffect = Read();

            //Green lightbar, everything off
            _outputOverride = new DualSenseOutputState { LightbarGreen = 255, LightbarBlue = 0 };
            _outputSignal.Set();
            await Task.Delay(600);
            ulong afterOff = Read();

            var result = Diagnostics.EvaluateTriggers(baseline, withEffect, afterOff);
            string detail = result == CheckResult.Pass
                ? "The controller confirmed the trigger effects, so lightbar, rumble and triggers are reaching it"
                : "The controller did not confirm the test effects. Don't touch the triggers during the test and try again.";

            checks.Add(new DiagnosticCheck("Controller accepts output", result,
                $"{detail} ({Diagnostics.Hex(baseline)} / {Diagnostics.Hex(withEffect)} / {Diagnostics.Hex(afterOff)})"));

            Console.WriteLine($"Self-test trigger feedback: {Diagnostics.Hex(baseline)} -> {Diagnostics.Hex(withEffect)} -> {Diagnostics.Hex(afterOff)}");
        }
        finally
        {
            _outputOverride = null;
            _selfTestRunning = false;
            RequestOutput();
        }

        return new DiagnosticReport(checks);
    }

    private void HandleReport(DualSenseDevice device, DualSenseState state, DsButtons previous)
    {
        var settings = _settings;
        DsButtons consumed = DsButtons.None;

        if (settings.MicMuteButtonControlsWindows)
        {
            consumed |= DsButtons.Mute;

            bool pressedNow = state.IsPressed(DsButtons.Mute) && (previous & DsButtons.Mute) == 0;

            if (pressedNow)
            {
                Task.Run(() =>
                {
                    _micMuted = MicMute.Toggle();
                    _outputSignal.Set();
                });
            }
        }

        if (settings.Touchpad.MouseEnabled)
        {
            consumed |= DsButtons.Touchpad;
            _touchpad.Update(state, settings.Touchpad);
        }
        else
        {
            _touchpad.ReleaseButtons();
        }

        var (gyroX, gyroY) = _gyro.Update(state, settings.Gyro, device.Calibration);

        PadState pad = InputMapper.Map(state, settings, consumed);
        pad.PassTouchpad = !settings.Touchpad.MouseEnabled;

        if (gyroX != 0f || gyroY != 0f)
        {
            pad.RightX = Math.Clamp(pad.RightX + gyroX, -1f, 1f);
            pad.RightY = Math.Clamp(pad.RightY + gyroY, -1f, 1f);
        }

        lock (_padLock)
        {
            _pad?.Update(pad);
        }

        _latestInput = state.Clone();
        _latestOutput = pad;

        if (state.HasExtendedData)
        {
            UpdateBattery(state);
        }
    }

    private void UpdateBattery(DualSenseState state)
    {
        var status = Status;

        if (status.HasBattery && status.BatteryPercent == state.BatteryPercent && status.Battery == state.Battery)
        {
            return;
        }

        _batteryEstimator.Add(_clock.Elapsed.TotalSeconds, state.BatteryPercent, state.Battery);
        var updated = UpdateStatus(s => s with
        {
            HasBattery = true,
            BatteryPercent = state.BatteryPercent,
            Battery = state.Battery,
            DrainPerHour = _batteryEstimator.DrainPerHour,
        });
        Console.WriteLine($"Battery: {updated.BatteryPercent}% ({updated.Battery})");

        int threshold = _settings.Battery.LowThreshold;

        if (updated.IsLowBattery(threshold))
        {
            if (!_lowBatteryNotified && _settings.Battery.NotifyWhenLow)
            {
                Notification?.Invoke($"Controller battery low ({updated.BatteryPercent}%).");
            }

            _lowBatteryNotified = true;
        }
        else if (updated.Battery != BatteryState.Discharging || updated.BatteryPercent > threshold + 5)
        {
            _lowBatteryNotified = false;
        }

        _outputSignal.Set();
    }

    private void OutputLoop()
    {
        DualSenseDevice? lastDevice = null;
        DualSenseOutputState? lastSent = null;
        long lastMicPoll = 0;

        while (!_stopping)
        {
            _outputSignal.WaitOne(IsLightbarAnimated() ? AnimatedOutputIntervalMs : OutputIntervalMs);

            if (_settings.Lightbar.Mode == LightbarMode.AudioReactive)
            {
                _audioLevel = _audioMeter.Read(_clock.ElapsedMilliseconds);
            }

            if (_stopping)
            {
                return;
            }

            var device = _device;

            if (device == null)
            {
                lastDevice = null;
                continue;
            }

            if (device != lastDevice)
            {
                lastDevice = device;
                lastSent = null;
            }

            //Native mode: the game drives lightbar, triggers and rumble. Only send right after the user
            //changed something here, and resend the full state then since the game may have changed it.
            if (!OutputPolicy.ShouldSend(_padMode, _clock.ElapsedMilliseconds, _userOutputUntilMs, _selfTestRunning))
            {
                lastSent = null;
                continue;
            }

            if (_settings.MicMuteButtonControlsWindows && _clock.ElapsedMilliseconds - lastMicPoll > MicPollIntervalMs)
            {
                lastMicPoll = _clock.ElapsedMilliseconds;
                _micMuted = MicMute.IsMuted() ?? _micMuted;
            }

            var desired = _outputOverride ?? ComputeOutput();

            if (desired == lastSent)
            {
                continue;
            }

            try
            {
                device.Send(desired);
                lastSent = desired;
            }
            catch (Exception ex)
            {
                //The input thread notices the disconnect and handles it
                Console.WriteLine($"Failed to send output report: {ex.Message}");
                lastSent = null;
                Thread.Sleep(200);
            }
        }
    }

    //Effects that change over time need more frequent output reports
    private bool IsLightbarAnimated()
    {
        var s = _settings;
        var status = Status;

        return s.Lightbar.Mode is LightbarMode.Breathing or LightbarMode.Rainbow or LightbarMode.AudioReactive
               || (s.Lightbar.PulseRedWhenMicMuted && s.MicMuteButtonControlsWindows && _micMuted)
               || (s.Battery.PulseLightbarWhenLow && status.IsLowBattery(s.Battery.LowThreshold));
    }

    public DualSenseOutputState ComputeOutput()
    {
        var s = _settings;
        var status = Status;
        double seconds = _clock.Elapsed.TotalSeconds;
        var custom = new RgbColor(s.Lightbar.Red, s.Lightbar.Green, s.Lightbar.Blue);

        RgbColor color = s.Lightbar.Mode switch
        {
            LightbarMode.Off => new RgbColor(0, 0, 0),
            LightbarMode.Battery when status.HasBattery => BatteryColor(status.BatteryPercent),
            LightbarMode.Game when _gameLightbar.HasValue => _gameLightbar.Value,
            LightbarMode.Rainbow => LightbarEffects.Rainbow(seconds, s.Lightbar.EffectSpeed),
            _ => custom,
        };

        float brightness = s.Lightbar.Brightness / 100f;

        if (s.Lightbar.Mode == LightbarMode.Breathing)
        {
            brightness *= LightbarEffects.Breathing(seconds, s.Lightbar.EffectSpeed);
        }
        else if (s.Lightbar.Mode == LightbarMode.AudioReactive)
        {
            brightness *= LightbarEffects.AudioBrightness(_audioLevel);
        }

        //Reminder that the mic is muted: slow red breathing
        if (s.Lightbar.PulseRedWhenMicMuted && s.MicMuteButtonControlsWindows && _micMuted)
        {
            color = new RgbColor(255, 0, 0);
            brightness = Math.Max(s.Lightbar.Brightness / 100f, 0.3f) * LightbarEffects.MicMutedPulse(seconds);
        }

        if (s.Battery.PulseLightbarWhenLow && status.IsLowBattery(s.Battery.LowThreshold))
        {
            //Slow red pulse
            double phase = _clock.Elapsed.TotalSeconds * Math.PI * 2 * 0.7;
            float pulse = (float)(0.15 + 0.85 * (0.5 + 0.5 * Math.Sin(phase)));
            color = new RgbColor(255, 0, 0);
            brightness = Math.Max(brightness, 0.3f) * pulse;
        }

        float rumble = s.RumbleStrength / 100f;
        bool testRumble = _clock.ElapsedMilliseconds < _testRumbleUntilMs;
        bool micControl = s.MicMuteButtonControlsWindows;

        return new DualSenseOutputState
        {
            RumbleLeft = (byte)((testRumble ? 255 : _rumbleLarge) * rumble),
            RumbleRight = (byte)((testRumble ? 255 : _rumbleSmall) * rumble),
            LightbarRed = (byte)(color.Red * brightness),
            LightbarGreen = (byte)(color.Green * brightness),
            LightbarBlue = (byte)(color.Blue * brightness),
            PlayerLeds = DualSenseOutputBuilder.PlayerLedPatterns[Math.Clamp(s.PlayerNumber, 0, 5)],
            MicLed = micControl && _micMuted ? MicLedMode.On : MicLedMode.Off,
            MicMuted = micControl && _micMuted,
            LeftTrigger = s.LeftTriggerEffect,
            RightTrigger = s.RightTriggerEffect,
        };
    }

    //Red at 0%, yellow at 50%, green at 100%
    public static RgbColor BatteryColor(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        byte red = (byte)(percent <= 50 ? 255 : 255 * (100 - percent) / 50);
        byte green = (byte)(percent >= 50 ? 255 : 255 * percent / 50);
        return new RgbColor(red, green, 0);
    }

    private ControllerStatus UpdateStatus(Func<ControllerStatus, ControllerStatus> change)
    {
        ControllerStatus updated;

        lock (_statusLock)
        {
            updated = change(_status);

            if (updated == _status)
            {
                return updated;
            }

            _status = updated;
        }

        StatusChanged?.Invoke(updated);
        return updated;
    }

    //Turns rumble and trigger resistance off and disconnects everything
    public void Stop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _outputSignal.Set();
        _outputThread?.Join(500);

        var device = _device;

        if (device != null)
        {
            try
            {
                device.Send(DualSenseOutputState.Neutral);
            }
            catch
            {
                //Controller already gone
            }

            //Unblocks the input thread's pending read
            device.Dispose();
        }

        _inputThread?.Join(1000);
        _touchpad.ReleaseButtons();

        lock (_padLock)
        {
            _pad?.Dispose();
            _pad = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _conflictTimer?.Dispose();
        _audioMeter.Dispose();
        _client.Dispose();
        _outputSignal.Dispose();
    }
}
