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
    string Emulation)
{
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
    bool MicMuted);

//Owns the controller connection, the virtual pad and every feature that runs per input report
public sealed class ControllerService : IDisposable
{
    private const int OutputIntervalMs = 50;
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
    private long _testRumbleUntilMs;

    //In native mode the game owns the outputs; the app only sends for a moment after the user changes something
    private long _userOutputUntilMs;
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
        _micMuted);

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
        }
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

        var updated = UpdateStatus(s => s with { HasBattery = true, BatteryPercent = state.BatteryPercent, Battery = state.Battery });
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
            _outputSignal.WaitOne(OutputIntervalMs);

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
            if (_padMode == EmulationMode.Native && _clock.ElapsedMilliseconds >= _userOutputUntilMs)
            {
                lastSent = null;
                continue;
            }

            if (_settings.MicMuteButtonControlsWindows && _clock.ElapsedMilliseconds - lastMicPoll > MicPollIntervalMs)
            {
                lastMicPoll = _clock.ElapsedMilliseconds;
                _micMuted = MicMute.IsMuted() ?? _micMuted;
            }

            var desired = ComputeOutput();

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

    public DualSenseOutputState ComputeOutput()
    {
        var s = _settings;
        var status = Status;

        RgbColor color = s.Lightbar.Mode switch
        {
            LightbarMode.Off => new RgbColor(0, 0, 0),
            LightbarMode.Battery when status.HasBattery => BatteryColor(status.BatteryPercent),
            LightbarMode.Game when _gameLightbar.HasValue => _gameLightbar.Value,
            _ => new RgbColor(s.Lightbar.Red, s.Lightbar.Green, s.Lightbar.Blue),
        };

        float brightness = s.Lightbar.Brightness / 100f;

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
        _client.Dispose();
        _outputSignal.Dispose();
    }
}
