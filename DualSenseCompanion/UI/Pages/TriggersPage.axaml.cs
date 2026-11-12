using Avalonia.Controls;
using DualSenseCompanion.App;

namespace DualSenseCompanion.UI.Pages;

public partial class TriggersPage : UserControl, ILivePage
{
    public TriggersPage() : this(null!)
    {
    }

    public TriggersPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public void Update(LiveSnapshot snapshot)
    {
        L2Fill.Width = L2Bar.Bounds.Width * (snapshot.Input?.L2 ?? 0) / 255.0;
        R2Fill.Width = R2Bar.Bounds.Width * (snapshot.Input?.R2 ?? 0) / 255.0;
    }
}
