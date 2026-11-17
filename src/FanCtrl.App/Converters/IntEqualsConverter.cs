using System.Globalization;
using System.Windows.Data;

namespace FanCtrl.App.Converters;

/// <summary>Marca un RadioButton cuando el valor entero coincide con el parámetro.</summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        return System.Convert.ToInt32(value, culture) == System.Convert.ToInt32(parameter, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
            return System.Convert.ToInt32(parameter, culture);

        return Binding.DoNothing;
    }
}
