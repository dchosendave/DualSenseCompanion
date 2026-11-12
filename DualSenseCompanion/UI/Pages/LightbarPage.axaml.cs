using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DualSenseCompanion.UI.Pages;

public partial class LightbarPage : UserControl
{
    private static readonly (string Name, Color Color)[] Presets =
    {
        ("Lightbar blue", Color.FromRgb(0x00, 0x50, 0xFF)),
        ("Cyan", Color.FromRgb(0x00, 0xD5, 0xFF)),
        ("Purple", Color.FromRgb(0x8B, 0x3D, 0xFF)),
        ("Pink", Color.FromRgb(0xFF, 0x2E, 0x9A)),
        ("Red", Color.FromRgb(0xFF, 0x10, 0x10)),
        ("Orange", Color.FromRgb(0xFF, 0x6A, 0x00)),
        ("Yellow", Color.FromRgb(0xFF, 0xD0, 0x00)),
        ("Green", Color.FromRgb(0x10, 0xE0, 0x40)),
        ("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    };

    private readonly SettingsViewModel _vm;

    public LightbarPage() : this(null!)
    {
    }

    public LightbarPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        foreach (var (name, color) in Presets)
        {
            var swatch = new Button { Background = new SolidColorBrush(color) };
            swatch.Classes.Add("swatch");
            ToolTip.SetTip(swatch, name);
            AutomationProperties.SetName(swatch, $"Lightbar color: {name}");
            swatch.Click += (s, e) => _vm.SetLightbarColor(color);
            Swatches.Children.Add(swatch);
        }
    }

    //Windows' own color picker
    private void MoreColors_Click(object? sender, RoutedEventArgs e)
    {
        var l = _vm.Settings.Lightbar;
        var custom = new int[16];
        var handle = GCHandle.Alloc(custom, GCHandleType.Pinned);

        try
        {
            var dialog = new ChooseColorData
            {
                Size = Marshal.SizeOf<ChooseColorData>(),
                Owner = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
                Result = l.Red | (l.Green << 8) | (l.Blue << 16),
                CustomColors = handle.AddrOfPinnedObject(),
                Flags = 0x1 | 0x2, //CC_RGBINIT | CC_FULLOPEN
            };

            if (ChooseColorW(ref dialog))
            {
                int rgb = dialog.Result;
                _vm.SetLightbarColor(Color.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF)));
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorData
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public int Result;
        public IntPtr CustomColors;
        public int Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ChooseColorW(ref ChooseColorData data);
}
