using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.UI.Pages;

public partial class ButtonsPage : UserControl, ILivePage
{
    private readonly SettingsViewModel _vm;
    private DsButtons _shown = (DsButtons)uint.MaxValue;

    public ButtonsPage() : this(null!)
    {
    }

    public ButtonsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    //Lights up the row of every button being pressed
    public void Update(LiveSnapshot snapshot)
    {
        var pressed = snapshot.Input?.Buttons ?? DsButtons.None;

        if (pressed == _shown)
        {
            return;
        }

        _shown = pressed;
        var accent = this.Brush("AccentBrush");
        var idle = this.Brush("TrackBrush");
        var glow = this.FindResource("AccentGlow") as IEffect;

        for (int i = 0; i < _vm.ButtonMap.Count; i++)
        {
            if (Rows.ContainerFromIndex(i) is not Control container)
            {
                continue;
            }

            var dot = container.GetVisualDescendants().OfType<Ellipse>().FirstOrDefault(e => e.Name == "Dot");

            if (dot != null)
            {
                bool down = (pressed & _vm.ButtonMap[i].Source) != 0;
                dot.Fill = down ? accent : idle;
                dot.Effect = down ? glow : null;
            }
        }
    }

    private void Reset_Click(object? sender, RoutedEventArgs e) => _vm.ResetButtonMap();
}
