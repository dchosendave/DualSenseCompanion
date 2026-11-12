using DualSenseCompanion;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.Tests;

public class ParserTests
{
    //Builds a full input report body (the 63 bytes after report ID / BT header)
    private static byte[] FullBody()
    {
        var d = new byte[63];
        d[0] = 10;   //LX
        d[1] = 20;   //LY
        d[2] = 30;   //RX
        d[3] = 40;   //RY
        d[4] = 50;   //L2
        d[5] = 60;   //R2
        d[7] = 0x20 | 0x02; //Cross + d-pad East
        d[8] = 0x01 | 0x20; //L1 + Options
        d[9] = 0x04;        //Mute
        d[15] = 0x34; d[16] = 0x12;  //GyroX = 0x1234
        d[17] = 0xFF; d[18] = 0xFF;  //GyroY = -1
        d[27] = 0x78; d[28] = 0x56; d[29] = 0x34; d[30] = 0x12; //timestamp
        //Touch 1 active, id 5, x = 0x345 (837), y = 0x2AB (683)
        d[32] = 0x05;
        d[33] = 0x45;
        d[34] = 0xB3;
        d[35] = 0x2A;
        d[36] = 0x80; //touch 2 inactive
        d[52] = 0x17; //charging, capacity 7
        return d;
    }

    private static void AssertFull(DualSenseState s)
    {
        Assert.True(s.HasExtendedData);
        Assert.Equal(10, s.LeftX);
        Assert.Equal(20, s.LeftY);
        Assert.Equal(30, s.RightX);
        Assert.Equal(40, s.RightY);
        Assert.Equal(50, s.L2);
        Assert.Equal(60, s.R2);
        Assert.True(s.IsPressed(DsButtons.Cross));
        Assert.True(s.IsPressed(DsButtons.DpadRight));
        Assert.False(s.IsPressed(DsButtons.DpadUp));
        Assert.True(s.IsPressed(DsButtons.L1));
        Assert.True(s.IsPressed(DsButtons.Options));
        Assert.True(s.IsPressed(DsButtons.Mute));
        Assert.True(s.IsPressed(DsButtons.TouchpadTouch));
        Assert.False(s.IsPressed(DsButtons.Square));
        Assert.Equal(0x1234, s.GyroX);
        Assert.Equal(-1, s.GyroY);
        Assert.Equal(0x12345678u, s.SensorTimestamp);
        Assert.True(s.Touch1.Active);
        Assert.Equal(5, s.Touch1.Id);
        Assert.Equal(0x345, s.Touch1.X);
        Assert.Equal(0x2AB, s.Touch1.Y);
        Assert.False(s.Touch2.Active);
        Assert.Equal(BatteryState.Charging, s.Battery);
        Assert.Equal(75, s.BatteryPercent);
    }

    [Fact]
    public void ParsesUsbFullReport()
    {
        var report = new byte[64];
        report[0] = 0x01;
        FullBody().CopyTo(report, 1);

        var state = new DualSenseState();
        Assert.True(DualSenseParser.TryParse(report, isBluetooth: false, state));
        AssertFull(state);
    }

    [Fact]
    public void ParsesBluetoothFullReport()
    {
        var report = new byte[78];
        report[0] = 0x31;
        report[1] = 0x10;
        FullBody().CopyTo(report, 2);

        var state = new DualSenseState();
        Assert.True(DualSenseParser.TryParse(report, isBluetooth: true, state));
        AssertFull(state);
    }

    [Fact]
    public void ParsesBluetoothSimpleReport()
    {
        var report = new byte[10];
        report[0] = 0x01;
        report[1] = 0;     //LX
        report[2] = 255;   //LY
        report[5] = 0x80 | 0x08; //Triangle, d-pad released
        report[6] = 0x02;  //R1
        report[7] = 0x01;  //PS
        report[8] = 200;   //L2
        report[9] = 100;   //R2

        var state = new DualSenseState();
        Assert.True(DualSenseParser.TryParse(report, isBluetooth: true, state));

        Assert.False(state.HasExtendedData);
        Assert.Equal(0, state.LeftX);
        Assert.Equal(255, state.LeftY);
        Assert.True(state.IsPressed(DsButtons.Triangle));
        Assert.True(state.IsPressed(DsButtons.R1));
        Assert.True(state.IsPressed(DsButtons.PS));
        Assert.False(state.IsPressed(DsButtons.DpadUp | DsButtons.DpadDown | DsButtons.DpadLeft | DsButtons.DpadRight));
        Assert.Equal(200, state.L2);
        Assert.Equal(100, state.R2);
    }

    [Theory]
    [InlineData(0, DsButtons.DpadUp)]
    [InlineData(1, DsButtons.DpadUp | DsButtons.DpadRight)]
    [InlineData(4, DsButtons.DpadDown)]
    [InlineData(5, DsButtons.DpadDown | DsButtons.DpadLeft)]
    [InlineData(7, DsButtons.DpadUp | DsButtons.DpadLeft)]
    [InlineData(8, DsButtons.None)]
    public void DecodesDpadHat(int hat, DsButtons expected)
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[8] = (byte)hat;

        var state = new DualSenseState();
        DualSenseParser.TryParse(report, false, state);

        var dpad = state.Buttons & (DsButtons.DpadUp | DsButtons.DpadDown | DsButtons.DpadLeft | DsButtons.DpadRight);
        Assert.Equal(expected, dpad);
    }

    [Fact]
    public void RejectsUnknownReports()
    {
        Assert.False(DualSenseParser.TryParse(new byte[64] { 0x09, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false, new DualSenseState()));
        Assert.False(DualSenseParser.TryParse(new byte[5], false, new DualSenseState()));
    }
}

public class OutputTests
{
    private static readonly DualSenseOutputState Sample = new DualSenseOutputState
    {
        RumbleLeft = 200,
        RumbleRight = 100,
        LightbarRed = 1,
        LightbarGreen = 2,
        LightbarBlue = 3,
        PlayerLeds = 0x04,
        MicLed = MicLedMode.On,
        MicMuted = true,
        RightTrigger = TriggerPreset.Hard,
    };

    [Fact]
    public void UsbLayoutMatchesExistingRumbleOffsets()
    {
        var r = DualSenseOutputBuilder.Build(Sample, bluetooth: false, lightbarSetup: false);

        Assert.Equal(48, r.Length);
        Assert.Equal(0x02, r[0]);
        Assert.Equal(100, r[3]); //right motor, same offset as the original USB rumble code
        Assert.Equal(200, r[4]); //left motor
        Assert.Equal(1, r[9]);   //mic LED
        Assert.Equal(0x10, r[10]); //mic mute
        Assert.Equal(TriggerEffects.FromPreset(TriggerPreset.Hard), r[11..22]);
        Assert.Equal(TriggerEffects.Off(), r[22..33]);
        Assert.Equal(0x04, r[44]);
        Assert.Equal(new byte[] { 1, 2, 3 }, r[45..48]);
    }

    [Fact]
    public void BluetoothLayoutMatchesExistingRumbleOffsetsAndHasValidCrc()
    {
        var r = DualSenseOutputBuilder.Build(Sample, bluetooth: true, lightbarSetup: true);

        Assert.Equal(78, r.Length);
        Assert.Equal(0x31, r[0]);
        Assert.Equal(0x02, r[1]);
        Assert.Equal(100, r[4]); //same offsets as the original BT rumble code
        Assert.Equal(200, r[5]);
        Assert.Equal(0x06, r[40]); //lightbar setup + vibration2, as before
        Assert.Equal(0x02, r[43]); //lightbar setup: fade out the blue light
        Assert.Equal(new byte[] { 1, 2, 3 }, r[46..49]);

        uint expected = Crc32.ComputeWithBluetoothOutputPrefix(r.AsSpan(0, 74));
        Assert.Equal(expected, BitConverter.ToUInt32(r, 74));
    }

    [Fact]
    public void BluetoothReportIsAlways78BytesWithChecksumAt74()
    {
        //Regression: sizing from the device's max output length (547 over Bluetooth) broke all output
        var r = DualSenseOutputBuilder.Build(Sample, bluetooth: true, lightbarSetup: false);

        Assert.Equal(78, r.Length);
        Assert.Equal(Crc32.ComputeWithBluetoothOutputPrefix(r.AsSpan(0, 74)), BitConverter.ToUInt32(r, 74));
    }

    [Fact]
    public void LightbarSetupOnlyWhenRequested()
    {
        var r = DualSenseOutputBuilder.Build(Sample, bluetooth: false, lightbarSetup: false);
        Assert.Equal(0x04, r[39]);
        Assert.Equal(0, r[42]);
    }

    [Fact]
    public void ExistingCrcTableEqualsStandardCrc32WithA2Prefix()
    {
        var data = new byte[74];
        new Random(42).NextBytes(data);
        Assert.Equal(Crc32.ComputeWithBluetoothOutputPrefix(data), NewCrc.ComputeCRC32(data, data.Length));
    }
}

//Reference implementation: standard CRC-32 over 0xA2 (BT output header) followed by the report
internal static class Crc32
{
    public static uint ComputeWithBluetoothOutputPrefix(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        crc = Step(crc, 0xA2);

        foreach (byte b in data)
        {
            crc = Step(crc, b);
        }

        return ~crc;
    }

    private static uint Step(uint crc, byte b)
    {
        crc ^= b;

        for (int i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }

        return crc;
    }
}

public class TriggerEffectTests
{
    [Fact]
    public void FeedbackFullStrengthFromStart()
    {
        var e = TriggerEffects.Feedback(0, 8);
        //10 zones active, force 7 in every 3-bit slot (30 bits set)
        Assert.Equal(new byte[] { 0x21, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 0, 0 }, e);
    }

    [Fact]
    public void WeaponEffect()
    {
        var e = TriggerEffects.Weapon(3, 6, 8);
        Assert.Equal(new byte[] { 0x25, 0x48, 0x00, 0x07, 0, 0, 0, 0, 0, 0, 0 }, e);
    }

    [Fact]
    public void VibrationEffect()
    {
        var e = TriggerEffects.Vibration(9, 1, 30);
        Assert.Equal(0x26, e[0]);
        Assert.Equal(0x00, e[1]);
        Assert.Equal(0x02, e[2]); //zone 9 only
        Assert.Equal(30, e[9]);
    }

    [Fact]
    public void AllPresetsAreElevenBytes()
    {
        foreach (TriggerPreset p in Enum.GetValues<TriggerPreset>())
        {
            Assert.Equal(TriggerEffects.Size, TriggerEffects.FromPreset(p).Length);
        }
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffects.Feedback(10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffects.Weapon(5, 5, 1));
    }
}

public class CalibrationTests
{
    [Fact]
    public void ParsesTypicalCalibration()
    {
        var buf = new byte[41];
        buf[0] = 0x05;

        void Put(int offset, short value) => BitConverter.GetBytes(value).CopyTo(buf, offset);

        Put(1, 0); Put(3, 0); Put(5, 0);       //biases
        Put(7, 8800); Put(9, -8800);            //pitch +/-
        Put(11, 8800); Put(13, -8800);          //yaw +/-
        Put(15, 8800); Put(17, -8800);          //roll +/-
        Put(19, 540); Put(21, 540);             //speed +/-

        var cal = DualSenseDevice.ParseCalibration(buf);

        Assert.NotNull(cal);
        Assert.Equal(1080f / 17600f, cal!.YawScale, 5);
    }

    [Fact]
    public void FallsBackOnGarbage()
    {
        var buf = new byte[41];
        buf[0] = 0x05;
        var cal = DualSenseDevice.ParseCalibration(buf);
        Assert.Equal(GyroCalibration.DefaultScale, cal!.PitchScale);
        Assert.Null(DualSenseDevice.ParseCalibration(new byte[41]));
    }
}
