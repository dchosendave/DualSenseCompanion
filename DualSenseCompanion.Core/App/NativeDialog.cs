using System.Runtime.InteropServices;

namespace DualSenseCompanion.App;

//Plain Windows message boxes, usable before any UI framework is running
public static class NativeDialog
{
    private const uint MB_OK = 0x0;
    private const uint MB_YESNO = 0x4;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_ICONQUESTION = 0x20;
    private const uint MB_ICONINFORMATION = 0x40;
    private const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    public static bool AskYesNo(string text, string caption) =>
        MessageBoxW(IntPtr.Zero, text, caption, MB_YESNO | MB_ICONQUESTION) == IDYES;

    public static void ShowError(string text, string caption) =>
        MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONERROR);

    public static void ShowInfo(string text, string caption) =>
        MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONINFORMATION);
}
