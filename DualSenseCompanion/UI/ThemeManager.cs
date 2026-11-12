using System.Windows;
using DualSenseCompanion.App;
using Microsoft.Win32;

namespace DualSenseCompanion.UI;

//Swaps the color palette at runtime. Brushes are referenced with DynamicResource, so the whole UI updates live.
public static class ThemeManager
{
    private static ResourceDictionary? _colors;
    private static AppTheme _theme = AppTheme.System;
    private static bool _listening;

    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Apply(AppTheme theme)
    {
        _theme = theme;
        bool dark = theme == AppTheme.Dark || (theme == AppTheme.System && WindowsUsesDarkApps());

        var app = Application.Current;

        if (app == null || (_colors != null && dark == IsDark))
        {
            return;
        }

        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/DualSenseCompanion;component/UI/Colors.{(dark ? "Dark" : "Light")}.xaml"),
        };

        var merged = app.Resources.MergedDictionaries;

        if (_colors != null)
        {
            merged.Remove(_colors);
        }

        merged.Insert(0, palette);
        _colors = palette;
        IsDark = dark;

        if (!_listening)
        {
            _listening = true;

            //Follow Windows when the user switches light/dark there
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _theme == AppTheme.System)
                {
                    app.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
                }
            };
        }

        Changed?.Invoke();
    }

    //Settings > Personalization > Colors > "Choose your default app mode"
    public static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
