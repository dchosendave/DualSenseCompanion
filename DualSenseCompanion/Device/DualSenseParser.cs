using System.Buffers.Binary;

namespace DualSenseCompanion.Device;

//Decodes DualSense input reports. Layout reference: Linux hid-playstation.c
public static class DualSenseParser
{
    public const int TouchpadWidth = 1920;
    public const int TouchpadHeight = 1080;

    //Size of the full input structure (without report ID / BT header)
    private const int FullReportSize = 63;

    public static bool TryParse(ReadOnlySpan<byte> report, bool isBluetooth, DualSenseState state)
    {
        if (report.Length < 10)
        {
            return false;
        }

        switch (report[0])
        {
            //Bluetooth full report: [0]=0x31, [1]=sequence/tag, data starts at [2]
            case 0x31 when report.Length >= 2 + FullReportSize:
                ParseFull(report.Slice(2), state);
                return true;

            //USB full report: [0]=0x01, data starts at [1]
            case 0x01 when !isBluetooth && report.Length >= 1 + FullReportSize:
                ParseFull(report.Slice(1), state);
                return true;

            //Bluetooth simple report, sent until the controller is switched to full mode
            case 0x01 when isBluetooth:
                ParseSimple(report, state);
                return true;

            default:
                return false;
        }
    }

    private static void ParseFull(ReadOnlySpan<byte> d, DualSenseState s)
    {
        s.LeftX = d[0];
        s.LeftY = d[1];
        s.RightX = d[2];
        s.RightY = d[3];
        s.L2 = d[4];
        s.R2 = d[5];

        s.Buttons = DecodeButtons(d[7], d[8], d[9]);

        s.GyroX = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(15));
        s.GyroY = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(17));
        s.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(19));
        s.AccelX = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(21));
        s.AccelY = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(23));
        s.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(25));
        s.SensorTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(27));

        s.Touch1 = DecodeTouch(d.Slice(32, 4));
        s.Touch2 = DecodeTouch(d.Slice(36, 4));

        if (s.Touch1.Active || s.Touch2.Active)
        {
            s.Buttons |= DsButtons.TouchpadTouch;
        }

        DecodeBattery(d[52], s);
        s.HasExtendedData = true;
    }

    private static void ParseSimple(ReadOnlySpan<byte> r, DualSenseState s)
    {
        s.LeftX = r[1];
        s.LeftY = r[2];
        s.RightX = r[3];
        s.RightY = r[4];
        s.Buttons = DecodeButtons(r[5], r[6], r[7]);
        s.L2 = r[8];
        s.R2 = r[9];
        s.HasExtendedData = false;
    }

    private static DsButtons DecodeButtons(byte b0, byte b1, byte b2)
    {
        DsButtons buttons = DsButtons.None;

        //Low nibble of b0 is the d-pad hat: 0=N, 1=NE, 2=E ... 7=NW, 8=released
        int hat = b0 & 0x0F;
        if (hat == 0 || hat == 1 || hat == 7) buttons |= DsButtons.DpadUp;
        if (hat == 1 || hat == 2 || hat == 3) buttons |= DsButtons.DpadRight;
        if (hat == 3 || hat == 4 || hat == 5) buttons |= DsButtons.DpadDown;
        if (hat == 5 || hat == 6 || hat == 7) buttons |= DsButtons.DpadLeft;

        if ((b0 & 0x10) != 0) buttons |= DsButtons.Square;
        if ((b0 & 0x20) != 0) buttons |= DsButtons.Cross;
        if ((b0 & 0x40) != 0) buttons |= DsButtons.Circle;
        if ((b0 & 0x80) != 0) buttons |= DsButtons.Triangle;

        if ((b1 & 0x01) != 0) buttons |= DsButtons.L1;
        if ((b1 & 0x02) != 0) buttons |= DsButtons.R1;
        if ((b1 & 0x04) != 0) buttons |= DsButtons.L2;
        if ((b1 & 0x08) != 0) buttons |= DsButtons.R2;
        if ((b1 & 0x10) != 0) buttons |= DsButtons.Create;
        if ((b1 & 0x20) != 0) buttons |= DsButtons.Options;
        if ((b1 & 0x40) != 0) buttons |= DsButtons.L3;
        if ((b1 & 0x80) != 0) buttons |= DsButtons.R3;

        if ((b2 & 0x01) != 0) buttons |= DsButtons.PS;
        if ((b2 & 0x02) != 0) buttons |= DsButtons.Touchpad;
        if ((b2 & 0x04) != 0) buttons |= DsButtons.Mute;

        return buttons;
    }

    private static TouchPoint DecodeTouch(ReadOnlySpan<byte> t)
    {
        return new TouchPoint
        {
            Active = (t[0] & 0x80) == 0,
            Id = (byte)(t[0] & 0x7F),
            X = t[1] | ((t[2] & 0x0F) << 8),
            Y = (t[2] >> 4) | (t[3] << 4),
        };
    }

    private static void DecodeBattery(byte status, DualSenseState s)
    {
        int capacity = status & 0x0F;
        int charging = status >> 4;

        switch (charging)
        {
            case 0x0:
                s.Battery = BatteryState.Discharging;
                s.BatteryPercent = Math.Min(capacity * 10 + 5, 100);
                break;
            case 0x1:
                s.Battery = BatteryState.Charging;
                s.BatteryPercent = Math.Min(capacity * 10 + 5, 100);
                break;
            case 0x2:
                s.Battery = BatteryState.Full;
                s.BatteryPercent = 100;
                break;
            default:
                s.Battery = BatteryState.Error;
                s.BatteryPercent = 0;
                break;
        }
    }
}
