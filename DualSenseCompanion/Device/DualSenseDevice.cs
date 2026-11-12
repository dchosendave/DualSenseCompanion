using System.Buffers.Binary;
using HidSharp;

namespace DualSenseCompanion.Device;

//Gyro scale factors read from the controller's factory calibration (feature report 0x05)
public sealed class GyroCalibration
{
    //Degrees per second per raw unit, per axis (pitch, yaw, roll)
    public float PitchScale { get; init; } = DefaultScale;
    public float YawScale { get; init; } = DefaultScale;
    public float RollScale { get; init; } = DefaultScale;

    //+-2000 deg/s over a signed 16-bit range
    public const float DefaultScale = 2000f / 32768f;

    public static GyroCalibration Default { get; } = new GyroCalibration();
}

public sealed class DualSenseDevice : IDisposable
{
    public const int VendorId = 0x054C;
    public const int DualSenseProductId = 0x0CE6;
    public const int DualSenseEdgeProductId = 0x0DF2;

    private readonly HidDevice _hid;
    private readonly object _writeLock = new object();
    private HidStream? _stream;
    private bool _lightbarSetupPending = true;
    private int _outputReportLength;

    public bool IsBluetooth { get; }
    public bool IsEdge => _hid.ProductID == DualSenseEdgeProductId;
    public string ConnectionName => IsBluetooth ? "Bluetooth" : "USB";
    public GyroCalibration Calibration { get; private set; } = GyroCalibration.Default;
    public int InputReportLength => _hid.GetMaxInputReportLength();

    private DualSenseDevice(HidDevice hid)
    {
        _hid = hid;

        //USB exposes 64-byte feature reports; Bluetooth uses larger ones
        IsBluetooth = hid.GetMaxFeatureReportLength() != 64;
    }

    public static bool IsDualSense(int vendorId, int productId) =>
        vendorId == VendorId && (productId == DualSenseProductId || productId == DualSenseEdgeProductId);

    public static DualSenseDevice? TryFind()
    {
        var hid = DeviceList.Local.GetHidDevices().FirstOrDefault(d => IsDualSense(d.VendorID, d.ProductID));
        return hid == null ? null : new DualSenseDevice(hid);
    }

    public void Open()
    {
        _stream = _hid.Open();
        _stream.ReadTimeout = Timeout.Infinite;
        _outputReportLength = _hid.GetMaxOutputReportLength();
        _lightbarSetupPending = true;

        //Reading the calibration report also switches a Bluetooth controller to full (0x31) reports,
        //which carry gyro, touchpad and battery data
        Calibration = ReadCalibration() ?? GyroCalibration.Default;
    }

    public int Read(byte[] buffer)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException("Device is not open.");
        }

        return _stream.Read(buffer, 0, buffer.Length);
    }

    public void Send(DualSenseOutputState state)
    {
        lock (_writeLock)
        {
            if (_stream == null)
            {
                return;
            }

            byte[] report = DualSenseOutputBuilder.Build(state, IsBluetooth, _outputReportLength, _lightbarSetupPending);
            _stream.Write(report, 0, report.Length);
            _lightbarSetupPending = false;
        }
    }

    private GyroCalibration? ReadCalibration()
    {
        try
        {
            var buffer = new byte[Math.Max(_hid.GetMaxFeatureReportLength(), 41)];
            buffer[0] = 0x05;
            _stream!.GetFeature(buffer);
            return ParseCalibration(buffer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read gyro calibration, using defaults: {ex.Message}");
            return null;
        }
    }

    public static GyroCalibration? ParseCalibration(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < 23 || buf[0] != 0x05)
        {
            return null;
        }

        var v = new short[11];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(1 + i * 2));
        }

        short pitchBias = v[0], yawBias = v[1], rollBias = v[2];
        short pitchPlus = v[3], pitchMinus = v[4];
        short yawPlus = v[5], yawMinus = v[6];
        short rollPlus = v[7], rollMinus = v[8];
        int speed2x = v[9] + v[10];

        float Scale(short plus, short minus, short bias)
        {
            int range = Math.Abs(plus - bias) + Math.Abs(minus - bias);
            if (range == 0 || speed2x <= 0)
            {
                return GyroCalibration.DefaultScale;
            }

            float scale = (float)speed2x / range;

            //Reject obviously broken calibration data
            return scale is > 0.01f and < 0.5f ? scale : GyroCalibration.DefaultScale;
        }

        return new GyroCalibration
        {
            PitchScale = Scale(pitchPlus, pitchMinus, pitchBias),
            YawScale = Scale(yawPlus, yawMinus, yawBias),
            RollScale = Scale(rollPlus, rollMinus, rollBias),
        };
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
