namespace DualSenseCompanion.Emulation;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue);

public interface IVirtualPad : IDisposable
{
    string Name { get; }

    //Large (low-frequency) motor, small (high-frequency) motor
    event Action<byte, byte>? RumbleReceived;

    //Lightbar color requested by the game (DualShock 4 only)
    event Action<RgbColor>? LightbarReceived;

    void Update(PadState state);

    //Releases all buttons and centers sticks
    void Reset();
}
