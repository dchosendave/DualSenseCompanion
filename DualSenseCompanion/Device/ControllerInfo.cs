using System.Buffers.Binary;
using System.Text;

namespace DualSenseCompanion.Device;

//Identity and firmware details read from the controller's feature reports (layout from Linux hid-playstation.c)
public sealed record ControllerInfo(
    string Model,
    string Connection,
    string? BluetoothAddress,
    string? UpdateVersion,
    string? FirmwareVersion,
    string? HardwareVersion,
    string? BuildDate)
{
    public const byte PairingReportId = 0x09;
    public const byte FirmwareReportId = 0x20;

    //Report 0x09: bytes 1-6 hold the controller's Bluetooth address, least significant byte first
    public static string? ParseBluetoothAddress(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != PairingReportId)
        {
            return null;
        }

        var bytes = report.Slice(1, 6).ToArray();

        if (bytes.All(b => b == 0))
        {
            return null;
        }

        Array.Reverse(bytes);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    //Report 0x20: build date/time text, hardware version, firmware version and the "update version" Sony shows
    public static (string? Update, string? Firmware, string? Hardware, string? BuildDate) ParseFirmware(ReadOnlySpan<byte> report)
    {
        if (report.Length < 46 || report[0] != FirmwareReportId)
        {
            return (null, null, null, null);
        }

        string date = Ascii(report.Slice(1, 11));
        string time = Ascii(report.Slice(12, 8));
        uint hardware = BinaryPrimitives.ReadUInt32LittleEndian(report.Slice(24));
        uint firmware = BinaryPrimitives.ReadUInt32LittleEndian(report.Slice(28));
        ushort update = BinaryPrimitives.ReadUInt16LittleEndian(report.Slice(44));

        string? build = string.IsNullOrWhiteSpace(date) ? null : $"{date} {time}".Trim();

        //Shown like the PS5 does: 0x0430 -> "04.30"
        string? updateText = update == 0 ? null : $"{update >> 8:X2}.{update & 0xFF:X2}";

        return (updateText, firmware == 0 ? null : firmware.ToString("X8"), hardware == 0 ? null : hardware.ToString("X8"), build);
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();

        foreach (byte b in bytes)
        {
            if (b is >= 0x20 and < 0x7F)
            {
                text.Append((char)b);
            }
        }

        return text.ToString().Trim();
    }
}
