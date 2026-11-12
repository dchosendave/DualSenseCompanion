namespace DualSenseCompanion.Device;

[Flags]
public enum DsButtons : uint
{
    None = 0,
    Square = 1 << 0,
    Cross = 1 << 1,
    Circle = 1 << 2,
    Triangle = 1 << 3,
    L1 = 1 << 4,
    R1 = 1 << 5,
    L2 = 1 << 6,
    R2 = 1 << 7,
    Create = 1 << 8,
    Options = 1 << 9,
    L3 = 1 << 10,
    R3 = 1 << 11,
    PS = 1 << 12,
    Touchpad = 1 << 13,
    Mute = 1 << 14,
    DpadUp = 1 << 15,
    DpadDown = 1 << 16,
    DpadLeft = 1 << 17,
    DpadRight = 1 << 18,

    //Not a physical button: set while a finger is on the touchpad
    TouchpadTouch = 1 << 19,
}

public enum BatteryState
{
    Unknown,
    Discharging,
    Charging,
    Full,
    Error,
}

public struct TouchPoint
{
    public bool Active;
    public byte Id;
    public int X; //0..1919
    public int Y; //0..1079
}

//One decoded input report
public sealed class DualSenseState
{
    public byte LeftX = 128;
    public byte LeftY = 128;
    public byte RightX = 128;
    public byte RightY = 128;
    public byte L2;
    public byte R2;

    public DsButtons Buttons;

    //Only filled by full reports (USB, or Bluetooth after switching to report 0x31)
    public bool HasExtendedData;
    public short GyroX; //pitch
    public short GyroY; //yaw
    public short GyroZ; //roll
    public short AccelX;
    public short AccelY;
    public short AccelZ;
    public uint SensorTimestamp; //units of 1/3 microsecond
    public TouchPoint Touch1;
    public TouchPoint Touch2;
    public int BatteryPercent;
    public BatteryState Battery = BatteryState.Unknown;

    public bool IsPressed(DsButtons button) => (Buttons & button) != 0;
}
