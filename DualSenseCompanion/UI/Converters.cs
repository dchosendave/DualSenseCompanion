using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DualSenseCompanion.UI;

//Binds a RadioButton to one value of an enum/int property: IsChecked="{Binding Mode, Converter={StaticResource Is}, ConverterParameter=Mouse}"
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null)
        {
            return Binding.DoNothing;
        }

        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string text = parameter.ToString()!;

        return type.IsEnum
            ? Enum.Parse(type, text, ignoreCase: true)
            : System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is true;
        return visible ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
