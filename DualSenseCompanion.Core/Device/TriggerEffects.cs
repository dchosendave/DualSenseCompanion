namespace DualSenseCompanion.Device;

public enum TriggerPreset
{
    Off,
    Soft,
    Medium,
    Hard,
    Weapon,
    Vibrate,
}

//Builds the 11-byte adaptive trigger effect blocks.
//Formats follow the community-documented "zone" effects (Nielk1's TriggerEffectGenerator).
public static class TriggerEffects
{
    public const int Size = 11;

    public static byte[] Off()
    {
        var e = new byte[Size];
        e[0] = 0x05;
        return e;
    }

    //Constant resistance from zone `position` (0-9) to the end, `strength` 1-8
    public static byte[] Feedback(int position, int strength)
    {
        if (position is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(position));
        if (strength is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(strength));

        uint forceValue = (uint)((strength - 1) & 0x07);
        uint forceZones = 0;
        ushort activeZones = 0;

        for (int i = position; i < 10; i++)
        {
            forceZones |= forceValue << (3 * i);
            activeZones |= (ushort)(1 << i);
        }

        var e = new byte[Size];
        e[0] = 0x21;
        e[1] = (byte)(activeZones & 0xFF);
        e[2] = (byte)(activeZones >> 8);
        e[3] = (byte)(forceZones & 0xFF);
        e[4] = (byte)((forceZones >> 8) & 0xFF);
        e[5] = (byte)((forceZones >> 16) & 0xFF);
        e[6] = (byte)((forceZones >> 24) & 0xFF);
        return e;
    }

    //Resistance between `start` (2-7) and `end` (start+1..8) that snaps like a trigger break
    public static byte[] Weapon(int start, int end, int strength)
    {
        if (start is < 2 or > 7) throw new ArgumentOutOfRangeException(nameof(start));
        if (end > 8 || end <= start) throw new ArgumentOutOfRangeException(nameof(end));
        if (strength is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(strength));

        ushort zones = (ushort)((1 << start) | (1 << end));

        var e = new byte[Size];
        e[0] = 0x25;
        e[1] = (byte)(zones & 0xFF);
        e[2] = (byte)(zones >> 8);
        e[3] = (byte)((strength - 1) & 0x07);
        return e;
    }

    //Vibration from zone `position` (0-9) onwards, `amplitude` 1-8, `frequency` in Hz
    public static byte[] Vibration(int position, int amplitude, int frequency)
    {
        if (position is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(position));
        if (amplitude is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(amplitude));
        if (frequency is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(frequency));

        uint strengthValue = (uint)((amplitude - 1) & 0x07);
        uint amplitudeZones = 0;
        ushort activeZones = 0;

        for (int i = position; i < 10; i++)
        {
            amplitudeZones |= strengthValue << (3 * i);
            activeZones |= (ushort)(1 << i);
        }

        var e = new byte[Size];
        e[0] = 0x26;
        e[1] = (byte)(activeZones & 0xFF);
        e[2] = (byte)(activeZones >> 8);
        e[3] = (byte)(amplitudeZones & 0xFF);
        e[4] = (byte)((amplitudeZones >> 8) & 0xFF);
        e[5] = (byte)((amplitudeZones >> 16) & 0xFF);
        e[6] = (byte)((amplitudeZones >> 24) & 0xFF);
        e[9] = (byte)frequency;
        return e;
    }

    public static byte[] FromPreset(TriggerPreset preset) => preset switch
    {
        TriggerPreset.Soft => Feedback(2, 3),
        TriggerPreset.Medium => Feedback(2, 5),
        TriggerPreset.Hard => Feedback(1, 8),
        TriggerPreset.Weapon => Weapon(3, 6, 8),
        TriggerPreset.Vibrate => Vibration(2, 6, 25),
        _ => Off(),
    };
}
