using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.UI.Pages;

public partial class ButtonsPage : UserControl, ILivePage
{
    private readonly SettingsViewModel _vm;
    private DsButtons _shown = (DsButtons)uint.MaxValue;

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
        var accent = (Brush)FindResource("AccentBrush");
        var idle = (Brush)FindResource("TrackBrush");

        for (int i = 0; i < _vm.ButtonMap.Count; i++)
        {
            if (Rows.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container ||
                VisualTreeHelper.GetChildrenCount(container) == 0)
            {
                continue;
            }

            if (container.ContentTemplate.FindName("Dot", container) is Ellipse dot)
            {
                bool down = (pressed & _vm.ButtonMap[i].Source) != 0;
                dot.Fill = down ? accent : idle;
                dot.Effect = down ? (System.Windows.Media.Effects.Effect)FindResource("AccentGlow") : null;
            }
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _vm.ResetButtonMap();
}
