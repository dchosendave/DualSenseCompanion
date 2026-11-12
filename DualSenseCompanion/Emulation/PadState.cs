using DualSenseCompanion.Device;

namespace DualSenseCompanion.Emulation;

//Logical buttons of the virtual controller. Names follow Xbox; DualShock 4 mode translates them.
[Flags]
public enum PadButtons : uint
{
    None = 0,
    A = 1 << 0,          //DS4: Cross
    B = 1 << 1,          //DS4: Circle
    X = 1 << 2,          //DS4: Square
    Y = 1 << 3,          //DS4: Triangle
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    LeftThumb = 1 << 6,
    RightThumb = 1 << 7,
    Back = 1 << 8,       //DS4: Share
    Start = 1 << 9,      //DS4: Options
    Guide = 1 << 10,     //DS4: PS
    Up = 1 << 11,
    Down = 1 << 12,
    Left = 1 << 13,
    Right = 1 << 14,
    Touchpad = 1 << 15,  //DS4 only
}

//What gets sent to the virtual controller
public sealed class PadState
{
    public PadButtons Buttons;

    //-1..1, Y positive = up
    public float LeftX;
    public float LeftY;
    public float RightX;
    public float RightY;

    //0..1
    public float LeftTrigger;
    public float RightTrigger;

    //Raw DualSense data for DualShock 4 passthrough (motion, touchpad, battery)
    public DualSenseState? Source;
    public bool PassTouchpad = true;

    public bool IsPressed(PadButtons button) => (Buttons & button) != 0;
}
