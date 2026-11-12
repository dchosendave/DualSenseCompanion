using Avalonia.Controls;
using Avalonia.Media;

namespace DualSenseCompanion.UI;

public static class ThemeResources
{
    //Looks up a resource for the control's current light/dark theme.
    //Plain FindResource doesn't search the theme dictionaries.
    public static IBrush? Brush(this Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) ? value as IBrush : null;
}
