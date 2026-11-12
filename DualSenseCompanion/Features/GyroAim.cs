using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.Features;

//Converts controller rotation into mouse movement or right-stick deflection
public sealed class GyroAim
{
    //Gyro drift is re-estimated after the controller has been still this long
    private const float StillRateThreshold = 2.5f;  //deg/s
    private const float StillTimeBeforeLearning = 1.5f; //seconds
    private const float BiasLearningRate = 0.02f;
    private const float ManualCalibrationSeconds = 1.0f;

    private readonly SubPixelAccumulator _mouse = new SubPixelAccumulator();
    private float _biasX, _biasY, _biasZ;
    private bool _hasBias;
    private float _stillTime;
    private uint? _lastTimestamp;

    private bool _toggledOn;
    private bool _toggleButtonWasDown;

    private float _calibrationTimeLeft;
    private double _calSumX, _calSumY, _calSumZ;
    private int _calSamples;

    public bool IsCalibrating => _calibrationTimeLeft > 0;
    public event Action<string>? Message;

    //Collects one second of samples; the controller should lie still
    public void StartCalibration()
    {
        _calibrationTimeLeft = ManualCalibrationSeconds;
        _calSumX = _calSumY = _calSumZ = 0;
        _calSamples = 0;
    }

    public void ResetConnection()
    {
        _lastTimestamp = null;
        _stillTime = 0;
        _mouse.Reset();
        _toggledOn = false;
        _toggleButtonWasDown = false;
    }

    //Returns right-stick offset (-1..1) when in stick mode; moves the mouse itself in mouse mode
    public (float StickX, float StickY) Update(DualSenseState s, GyroSettings g, GyroCalibration cal)
    {
        if (!s.HasExtendedData)
        {
            return (0f, 0f);
        }

        float dt = DeltaSeconds(s.SensorTimestamp);

        if (IsCalibrating)
        {
            CollectCalibration(s, dt);
            return (0f, 0f);
        }

        if (!_hasBias)
        {
            //Start from the first sample; refined automatically once the controller is still
            _biasX = s.GyroX;
            _biasY = s.GyroY;
            _biasZ = s.GyroZ;
            _hasBias = true;
        }

        float pitch = (s.GyroX - _biasX) * cal.PitchScale;
        float yaw = (s.GyroY - _biasY) * cal.YawScale;
        float roll = (s.GyroZ - _biasZ) * cal.RollScale;

        LearnBias(s, pitch, yaw, roll, dt);

        bool active = IsActive(s, g);

        if (g.Mode == GyroMode.Off || !active)
        {
            _mouse.Reset();
            return (0f, 0f);
        }

        //Turning right (clockwise seen from above) gives negative yaw; tilting the top away gives negative pitch
        float horizontal = g.HorizontalAxis == GyroAxis.Roll ? -roll : -yaw;
        float vertical = pitch;

        horizontal = Soften(horizontal, g.SmoothingThreshold);
        vertical = Soften(vertical, g.SmoothingThreshold);

        if (g.InvertX) horizontal = -horizontal;
        if (g.InvertY) vertical = -vertical;

        if (g.Mode == GyroMode.Mouse)
        {
            //Screen Y grows downwards
            var (dx, dy) = _mouse.Add(horizontal * dt * g.MouseSensitivity, -vertical * dt * g.MouseSensitivity);
            MouseInput.Move(dx, dy);
            return (0f, 0f);
        }

        return ToStick(horizontal, vertical, g);
    }

    public static (float X, float Y) ToStick(float horizontalRate, float verticalRate, GyroSettings g)
    {
        float x = horizontalRate / g.StickFullSpeed;
        float y = verticalRate / g.StickFullSpeed;
        float magnitude = MathF.Sqrt(x * x + y * y);

        if (magnitude < 0.0001f)
        {
            return (0f, 0f);
        }

        float output = Math.Min(1f, g.StickAntiDeadzone + (1f - g.StickAntiDeadzone) * Math.Min(magnitude, 1f));
        float scale = output / magnitude;
        return (Math.Clamp(x * scale, -1f, 1f), Math.Clamp(y * scale, -1f, 1f));
    }

    //Scales down tiny movements (hand tremor) without a hard cutoff
    public static float Soften(float rate, float threshold)
    {
        float abs = Math.Abs(rate);

        if (threshold <= 0f || abs >= threshold)
        {
            return rate;
        }

        return rate * (abs / threshold);
    }

    private bool IsActive(DualSenseState s, GyroSettings g)
    {
        bool buttonDown = (s.Buttons & g.ActivationButton) != 0;

        switch (g.Activation)
        {
            case GyroActivation.Always:
                return true;

            case GyroActivation.WhileHeld:
                return buttonDown;

            case GyroActivation.Toggle:
                if (buttonDown && !_toggleButtonWasDown)
                {
                    _toggledOn = !_toggledOn;
                }
                _toggleButtonWasDown = buttonDown;
                return _toggledOn;

            default:
                return false;
        }
    }

    private float DeltaSeconds(uint timestamp)
    {
        float dt = 1f / 250f;

        if (_lastTimestamp.HasValue)
        {
            //Wraps around naturally with unsigned subtraction; 3 ticks per microsecond
            dt = (timestamp - _lastTimestamp.Value) / 3_000_000f;
        }

        _lastTimestamp = timestamp;
        return dt is > 0f and < 0.05f ? dt : 1f / 250f;
    }

    private void LearnBias(DualSenseState s, float pitch, float yaw, float roll, float dt)
    {
        bool still = Math.Abs(pitch) < StillRateThreshold && Math.Abs(yaw) < StillRateThreshold && Math.Abs(roll) < StillRateThreshold;

        if (!still)
        {
            _stillTime = 0;
            return;
        }

        _stillTime += dt;

        if (_stillTime >= StillTimeBeforeLearning)
        {
            _biasX += (s.GyroX - _biasX) * BiasLearningRate;
            _biasY += (s.GyroY - _biasY) * BiasLearningRate;
            _biasZ += (s.GyroZ - _biasZ) * BiasLearningRate;
        }
    }

    private void CollectCalibration(DualSenseState s, float dt)
    {
        _calSumX += s.GyroX;
        _calSumY += s.GyroY;
        _calSumZ += s.GyroZ;
        _calSamples++;
        _calibrationTimeLeft -= dt;

        if (_calibrationTimeLeft <= 0 && _calSamples > 0)
        {
            _biasX = (float)(_calSumX / _calSamples);
            _biasY = (float)(_calSumY / _calSamples);
            _biasZ = (float)(_calSumZ / _calSamples);
            _hasBias = true;
            _stillTime = 0;
            Message?.Invoke($"Gyro calibrated ({_calSamples} samples).");
        }
    }
}
