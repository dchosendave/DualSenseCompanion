using DualSenseCompanion.Device;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace DualSenseCompanion.Emulation;

//Emulates a DualShock 4 using the extended raw report, so games also get gyro, accelerometer and touchpad
public sealed class DualShock4Pad : IVirtualPad
{
    public const int ReportSize = 63;
    private const int Ds4TouchpadHeight = 943;

    private readonly IDualShock4Controller _controller;
    private readonly byte[] _report = new byte[ReportSize];
    private byte _touchPacketCounter;
    private ushort _timestamp;
    private uint? _lastSensorTimestamp;

    public string Name => "DualShock 4";
    public event Action<byte, byte>? RumbleReceived;
    public event Action<RgbColor>? LightbarReceived;

    public DualShock4Pad(ViGEmClient client)
    {
        _controller = client.CreateDualShock4Controller();
        _controller.AutoSubmitReport = false;
        //Deprecated in favour of AwaitRawOutputReport, but still delivers rumble and lightbar reliably for this use
#pragma warning disable CS0618
        _controller.FeedbackReceived += (s, e) =>
        {
            RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
            LightbarReceived?.Invoke(new RgbColor(e.LightbarColor.Red, e.LightbarColor.Green, e.LightbarColor.Blue));
        };
#pragma warning restore CS0618
        _controller.Connect();
    }

    public void Update(PadState state)
    {
        BuildReport(state, _report, ref _timestamp, ref _lastSensorTimestamp, ref _touchPacketCounter);
        _controller.SubmitRawReport(_report);
    }

    public void Reset()
    {
        ushort ts = 0;
        uint? last = null;
        byte counter = 0;
        BuildReport(new PadState(), _report, ref ts, ref last, ref counter);
        _controller.SubmitRawReport(_report);
    }

    //Layout of DS4_REPORT_EX from ViGEmBus
    public static void BuildReport(PadState state, byte[] r, ref ushort timestamp, ref uint? lastSensorTimestamp, ref byte touchPacketCounter)
    {
        Array.Clear(r);

        r[0] = ToStick(state.LeftX);
        r[1] = ToStick(-state.LeftY);
        r[2] = ToStick(state.RightX);
        r[3] = ToStick(-state.RightY);

        byte leftTrigger = Xbox360Pad.ToTrigger(state.LeftTrigger);
        byte rightTrigger = Xbox360Pad.ToTrigger(state.RightTrigger);

        ushort buttons = DpadHat(state);
        if (state.IsPressed(PadButtons.X)) buttons |= 1 << 4;              //Square
        if (state.IsPressed(PadButtons.A)) buttons |= 1 << 5;              //Cross
        if (state.IsPressed(PadButtons.B)) buttons |= 1 << 6;              //Circle
        if (state.IsPressed(PadButtons.Y)) buttons |= 1 << 7;              //Triangle
        if (state.IsPressed(PadButtons.LeftShoulder)) buttons |= 1 << 8;   //L1
        if (state.IsPressed(PadButtons.RightShoulder)) buttons |= 1 << 9;  //R1
        if (leftTrigger > 0) buttons |= 1 << 10;                           //L2
        if (rightTrigger > 0) buttons |= 1 << 11;                          //R2
        if (state.IsPressed(PadButtons.Back)) buttons |= 1 << 12;          //Share
        if (state.IsPressed(PadButtons.Start)) buttons |= 1 << 13;         //Options
        if (state.IsPressed(PadButtons.LeftThumb)) buttons |= 1 << 14;     //L3
        if (state.IsPressed(PadButtons.RightThumb)) buttons |= 1 << 15;    //R3

        r[4] = (byte)(buttons & 0xFF);
        r[5] = (byte)(buttons >> 8);

        byte special = 0;
        if (state.IsPressed(PadButtons.Guide)) special |= 0x01;
        if (state.IsPressed(PadButtons.Touchpad)) special |= 0x02;
        r[6] = special;

        r[7] = leftTrigger;
        r[8] = rightTrigger;

        var ds = state.Source;

        if (ds != null && ds.HasExtendedData)
        {
            //DualSense timestamps tick every 1/3 us, DualShock 4 every 16/3 us
            if (lastSensorTimestamp.HasValue)
            {
                timestamp += (ushort)((ds.SensorTimestamp - lastSensorTimestamp.Value) / 16);
            }
            lastSensorTimestamp = ds.SensorTimestamp;

            //Both controllers use the same gyro/accelerometer units and axes
            WriteInt16(r, 12, ds.GyroX);
            WriteInt16(r, 14, ds.GyroY);
            WriteInt16(r, 16, ds.GyroZ);
            WriteInt16(r, 18, ds.AccelX);
            WriteInt16(r, 20, ds.AccelY);
            WriteInt16(r, 22, ds.AccelZ);

            //Battery: low nibble 0-10 (0-11 while charging), bit 4 = cable connected
            bool cable = ds.Battery is BatteryState.Charging or BatteryState.Full;
            int level = Math.Clamp(ds.BatteryPercent / 10, 0, cable ? 11 : 10);
            r[11] = (byte)Math.Clamp(ds.BatteryPercent * 255 / 100, 0, 255);
            r[29] = (byte)(level | (cable ? 0x10 : 0));

            //One touch packet with up to two fingers
            r[32] = 1;
            r[33] = touchPacketCounter++;
            WriteTouch(r, 34, state.PassTouchpad ? ds.Touch1 : default);
            WriteTouch(r, 38, state.PassTouchpad ? ds.Touch2 : default);
        }
        else
        {
            r[29] = 0x0A;
            r[32] = 0;
            WriteTouch(r, 34, default);
            WriteTouch(r, 38, default);
        }

        r[9] = (byte)(timestamp & 0xFF);
        r[10] = (byte)(timestamp >> 8);
    }

    private static ushort DpadHat(PadState s)
    {
        bool up = s.IsPressed(PadButtons.Up), down = s.IsPressed(PadButtons.Down);
        bool left = s.IsPressed(PadButtons.Left), right = s.IsPressed(PadButtons.Right);

        if (up && right) return 1;
        if (right && down) return 3;
        if (down && left) return 5;
        if (left && up) return 7;
        if (up) return 0;
        if (right) return 2;
        if (down) return 4;
        if (left) return 6;
        return 8;
    }

    //-1..1 to 0..255 with 128 as center
    public static byte ToStick(float value) =>
        (byte)Math.Clamp(MathF.Round(128f + value * (value >= 0 ? 127f : 128f)), 0f, 255f);

    private static void WriteInt16(byte[] r, int offset, short value)
    {
        r[offset] = (byte)(value & 0xFF);
        r[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteTouch(byte[] r, int offset, TouchPoint touch)
    {
        if (!touch.Active)
        {
            r[offset] = 0x80;
            return;
        }

        int x = Math.Clamp(touch.X, 0, 1919);
        int y = Math.Clamp(touch.Y * Ds4TouchpadHeight / DualSenseParser.TouchpadHeight, 0, Ds4TouchpadHeight - 1);

        r[offset] = (byte)(touch.Id & 0x7F);
        r[offset + 1] = (byte)(x & 0xFF);
        r[offset + 2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        r[offset + 3] = (byte)(y >> 4);
    }

    public void Dispose()
    {
        try
        {
            _controller.Disconnect();
        }
        catch
        {
            //Already gone
        }
    }
}
