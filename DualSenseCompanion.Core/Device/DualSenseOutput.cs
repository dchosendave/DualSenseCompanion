namespace DualSenseCompanion.Device;

public enum MicLedMode : byte
{
    Off = 0,
    On = 1,
    Pulse = 2,
}

//Everything the app can set on the controller. Always sent as a whole.
public sealed record DualSenseOutputState
{
    public byte RumbleLeft { get; init; }  //large / low-frequency motor
    public byte RumbleRight { get; init; } //small / high-frequency motor
    public byte LightbarRed { get; init; }
    public byte LightbarGreen { get; init; }
    public byte LightbarBlue { get; init; } = 255;
    public byte PlayerLeds { get; init; }  //5-bit mask, bit 0 = leftmost LED
    public MicLedMode MicLed { get; init; }
    public bool MicMuted { get; init; }   //mutes the controller's built-in mic in hardware
    public TriggerPreset LeftTrigger { get; init; }
    public TriggerPreset RightTrigger { get; init; }
    public int LeftTriggerStrength { get; init; } = 100;  //percent of the preset's force
    public int RightTriggerStrength { get; init; } = 100;

    //Turns everything off: used when the app exits so the controller isn't left with resistance on
    public static DualSenseOutputState Neutral { get; } = new DualSenseOutputState { LightbarBlue = 64 };
}

public static class DualSenseOutputBuilder
{
    //Player LED patterns as shown on a PS5 (center LED for player 1, etc.)
    public static readonly byte[] PlayerLedPatterns = { 0x00, 0x04, 0x0A, 0x15, 0x1B, 0x1F };

    //valid_flag0
    private const byte CompatibleVibration = 0x01;
    private const byte HapticsSelect = 0x02;
    private const byte RightTriggerEffect = 0x04;
    private const byte LeftTriggerEffect = 0x08;

    //valid_flag1
    private const byte MicMuteLedControl = 0x01;
    private const byte PowerSaveControl = 0x02;
    private const byte LightbarControl = 0x04;
    private const byte PlayerIndicatorControl = 0x10;

    //valid_flag2
    private const byte LightbarSetupControl = 0x02;
    private const byte CompatibleVibration2 = 0x04;

    private const byte PowerSaveMicMute = 0x10;
    private const byte LightbarSetupLightOut = 0x02;

    //Offset of the shared output block inside the report.
    //USB: [0]=0x02. Bluetooth: [0]=0x31, [1]=0x02 tag (same layout the app already used for BT rumble).
    public static int CommonOffset(bool bluetooth) => bluetooth ? 2 : 1;

    //Report 0x31 is exactly 78 bytes over Bluetooth (checksum in the last 4); report 0x02 is 48 bytes over USB.
    //Don't size it from the device's max output length: over Bluetooth that is 547 (larger audio/haptics reports),
    //which would put the checksum in the wrong place and the controller would ignore the report.
    public const int BluetoothReportLength = 78;
    public const int UsbReportLength = 48;

    public static byte[] Build(DualSenseOutputState s, bool bluetooth, bool lightbarSetup)
    {
        int reportLength = bluetooth ? BluetoothReportLength : UsbReportLength;
        var r = new byte[reportLength];
        int c = CommonOffset(bluetooth);

        if (bluetooth)
        {
            r[0] = 0x31;
            r[1] = 0x02;
        }
        else
        {
            r[0] = 0x02;
        }

        r[c + 0] = CompatibleVibration | HapticsSelect | RightTriggerEffect | LeftTriggerEffect;
        r[c + 1] = MicMuteLedControl | PowerSaveControl | LightbarControl | PlayerIndicatorControl;
        r[c + 2] = s.RumbleRight;
        r[c + 3] = s.RumbleLeft;
        r[c + 8] = (byte)s.MicLed;
        r[c + 9] = s.MicMuted ? PowerSaveMicMute : (byte)0;

        Array.Copy(TriggerEffects.FromPreset(s.RightTrigger, s.RightTriggerStrength), 0, r, c + 10, TriggerEffects.Size);
        Array.Copy(TriggerEffects.FromPreset(s.LeftTrigger, s.LeftTriggerStrength), 0, r, c + 21, TriggerEffects.Size);

        r[c + 38] = CompatibleVibration2;

        //Fades out the blue "connecting" light so our color takes over; only needed once per connection
        if (lightbarSetup)
        {
            r[c + 38] |= LightbarSetupControl;
            r[c + 41] = LightbarSetupLightOut;
        }

        r[c + 42] = 0x00; //player LED brightness: full
        r[c + 43] = (byte)(s.PlayerLeds & 0x1F);
        r[c + 44] = s.LightbarRed;
        r[c + 45] = s.LightbarGreen;
        r[c + 46] = s.LightbarBlue;

        if (bluetooth)
        {
            uint crc = NewCrc.ComputeCRC32(r, reportLength - 4);
            BitConverter.GetBytes(crc).CopyTo(r, reportLength - 4);
        }

        return r;
    }
}
