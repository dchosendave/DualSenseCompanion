using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.Emulation;

//Turns raw DualSense input into virtual controller state: deadzones, curves and button remapping
public static class InputMapper
{
    public static Dictionary<DsButtons, PadButtons> DefaultButtonMap() => new Dictionary<DsButtons, PadButtons>
    {
        [DsButtons.Cross] = PadButtons.A,
        [DsButtons.Circle] = PadButtons.B,
        [DsButtons.Square] = PadButtons.X,
        [DsButtons.Triangle] = PadButtons.Y,
        [DsButtons.L1] = PadButtons.LeftShoulder,
        [DsButtons.R1] = PadButtons.RightShoulder,
        [DsButtons.L3] = PadButtons.LeftThumb,
        [DsButtons.R3] = PadButtons.RightThumb,
        [DsButtons.Create] = PadButtons.Back,
        [DsButtons.Options] = PadButtons.Start,
        [DsButtons.PS] = PadButtons.Guide,
        [DsButtons.DpadUp] = PadButtons.Up,
        [DsButtons.DpadDown] = PadButtons.Down,
        [DsButtons.DpadLeft] = PadButtons.Left,
        [DsButtons.DpadRight] = PadButtons.Right,
        [DsButtons.Touchpad] = PadButtons.Touchpad,
        [DsButtons.Mute] = PadButtons.None,
    };

    //`consumed` holds buttons another feature is using (e.g. touchpad click as mouse click)
    public static PadState Map(DualSenseState ds, Settings settings, DsButtons consumed)
    {
        var pad = new PadState { Source = ds };

        (pad.LeftX, pad.LeftY) = ApplyStick(ds.LeftX, ds.LeftY, settings.LeftStick);
        (pad.RightX, pad.RightY) = ApplyStick(ds.RightX, ds.RightY, settings.RightStick);
        pad.LeftTrigger = ApplyTrigger(ds.L2, settings.TriggerDeadzone);
        pad.RightTrigger = ApplyTrigger(ds.R2, settings.TriggerDeadzone);

        DsButtons pressed = ds.Buttons & ~consumed;

        foreach (var (source, target) in settings.ButtonMap)
        {
            if (target != PadButtons.None && (pressed & source) != 0)
            {
                pad.Buttons |= target;
            }
        }

        return pad;
    }

    //Converts a 0-255 axis to -1..1 with 128 as exact center
    public static float NormalizeAxis(byte value) =>
        value >= 128 ? (value - 128) / 127f : (value - 128) / 128f;

    public static (float X, float Y) ApplyStick(byte rawX, byte rawY, StickSettings s)
    {
        float x = NormalizeAxis(rawX);
        float y = -NormalizeAxis(rawY); //DualSense Y grows downwards

        float magnitude = MathF.Sqrt(x * x + y * y);

        if (magnitude <= s.Deadzone || magnitude == 0f)
        {
            return (0f, 0f);
        }

        float outer = Math.Max(1f - s.OuterDeadzone, s.Deadzone + 0.01f);
        float t = Math.Clamp((magnitude - s.Deadzone) / (outer - s.Deadzone), 0f, 1f);
        t = MathF.Pow(t, s.Curve);
        t = s.AntiDeadzone + (1f - s.AntiDeadzone) * t;

        float scale = t / magnitude;
        return (Math.Clamp(x * scale, -1f, 1f), Math.Clamp(y * scale, -1f, 1f));
    }

    public static float ApplyTrigger(byte raw, float deadzone)
    {
        float v = raw / 255f;

        if (v <= deadzone)
        {
            return 0f;
        }

        return Math.Clamp((v - deadzone) / (1f - deadzone), 0f, 1f);
    }
}
