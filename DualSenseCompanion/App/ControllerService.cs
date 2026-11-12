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

    private readonly object _statusLock = new object();
    private ControllerStatus _status;

    public event Action<ControllerStatus>? StatusChanged;
    public event Action<string>? Notification;

    public ControllerStatus Status
    {
        get { lock (_statusLock) return _status; }
    }

    public Settings Settings => _settings;

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

        if (_settings.Emulation != _padMode)
        {
            CreatePad();
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

            IVirtualPad pad = _settings.Emulation == EmulationMode.DualShock4
                ? new DualShock4Pad(_client)
                : new Xbox360Pad(_client);

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

        UpdateStatus(s => s with { Emulation = _pad!.Name });
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

            CloakManager.HidePS5Controller();

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
        bool micControl = s.MicMuteButtonControlsWindows;

        return new DualSenseOutputState
        {
            RumbleLeft = (byte)(_rumbleLarge * rumble),
            RumbleRight = (byte)(_rumbleSmall * rumble),
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
