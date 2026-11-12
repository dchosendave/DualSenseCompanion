using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DualSenseCompanion.UI;

//Binds a RadioButton to one value of an enum/int property: IsChecked="{Binding Mode, Converter={StaticResource Is}, ConverterParameter=Mouse}"
public sealed class EqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null)
        {
            return BindingOperations.DoNothing;
        }

        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string text = parameter.ToString()!;

        return type.IsEnum
            ? Enum.Parse(type, text, ignoreCase: true)
            : System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }
}

//Formats numbers for the value labels next to sliders, e.g. ConverterParameter='{}{0:P0}'
public sealed class FormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Format(CultureInfo.CurrentCulture, parameter?.ToString() ?? "{0}", value);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
