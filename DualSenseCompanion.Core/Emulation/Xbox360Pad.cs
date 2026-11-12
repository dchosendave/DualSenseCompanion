using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DualSenseCompanion.Emulation;

public sealed class Xbox360Pad : IVirtualPad
{
    private static readonly (PadButtons Pad, Xbox360Button Xbox)[] ButtonMap =
    {
        (PadButtons.A, Xbox360Button.A),
        (PadButtons.B, Xbox360Button.B),
        (PadButtons.X, Xbox360Button.X),
        (PadButtons.Y, Xbox360Button.Y),
        (PadButtons.LeftShoulder, Xbox360Button.LeftShoulder),
        (PadButtons.RightShoulder, Xbox360Button.RightShoulder),
        (PadButtons.LeftThumb, Xbox360Button.LeftThumb),
        (PadButtons.RightThumb, Xbox360Button.RightThumb),
        (PadButtons.Back, Xbox360Button.Back),
        (PadButtons.Start, Xbox360Button.Start),
        (PadButtons.Guide, Xbox360Button.Guide),
        (PadButtons.Up, Xbox360Button.Up),
        (PadButtons.Down, Xbox360Button.Down),
        (PadButtons.Left, Xbox360Button.Left),
        (PadButtons.Right, Xbox360Button.Right),
    };

    private readonly IXbox360Controller _controller;

    public string Name => "Xbox 360";
    public event Action<byte, byte>? RumbleReceived;
    public event Action<RgbColor>? LightbarReceived { add { } remove { } }

    public Xbox360Pad(ViGEmClient client)
    {
        _controller = client.CreateXbox360Controller();

        //State changes are batched and sent once per update
        _controller.AutoSubmitReport = false;
        _controller.FeedbackReceived += (s, e) => RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
        _controller.Connect();
    }

    public void Update(PadState state)
    {
        foreach (var (pad, xbox) in ButtonMap)
        {
            _controller.SetButtonState(xbox, state.IsPressed(pad));
        }

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(state.LeftX));
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxis(state.LeftY));
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, ToAxis(state.RightX));
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, ToAxis(state.RightY));
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ToTrigger(state.LeftTrigger));
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, ToTrigger(state.RightTrigger));
        _controller.SubmitReport();
    }

    public void Reset()
    {
        _controller.ResetReport();
        _controller.SubmitReport();
    }

    public static short ToAxis(float value) =>
        (short)Math.Clamp(MathF.Round(value * 32767f), -32768f, 32767f);

    public static byte ToTrigger(float value) =>
        (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

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
