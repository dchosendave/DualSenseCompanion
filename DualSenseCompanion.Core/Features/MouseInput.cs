using System.Runtime.InteropServices;

namespace DualSenseCompanion.Features;

//Sends relative mouse movement, clicks and wheel events through SendInput
public static class MouseInput
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventWheel = 0x0800;
    public const int WheelDelta = 120;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInputData Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static void Move(int dx, int dy)
    {
        if (dx != 0 || dy != 0)
        {
            Send(new MouseInputData { Dx = dx, Dy = dy, Flags = MouseEventMove });
        }
    }

    public static void Wheel(int amount)
    {
        if (amount != 0)
        {
            Send(new MouseInputData { MouseData = unchecked((uint)amount), Flags = MouseEventWheel });
        }
    }

    public static void LeftButton(bool down) =>
        Send(new MouseInputData { Flags = down ? MouseEventLeftDown : MouseEventLeftUp });

    public static void RightButton(bool down) =>
        Send(new MouseInputData { Flags = down ? MouseEventRightDown : MouseEventRightUp });

    private static void Send(MouseInputData data)
    {
        var inputs = new[] { new Input { Type = InputMouse, Mouse = data } };
        SendInput(1, inputs, Marshal.SizeOf<Input>());
    }
}

//Keeps the fractional part of mouse movement so slow motion isn't lost to rounding
public sealed class SubPixelAccumulator
{
    private float _x;
    private float _y;

    public (int X, int Y) Add(float dx, float dy)
    {
        _x += dx;
        _y += dy;
        int ix = (int)_x;
        int iy = (int)_y;
        _x -= ix;
        _y -= iy;
        return (ix, iy);
    }

    public void Reset()
    {
        _x = 0;
        _y = 0;
    }
}
