using System.Globalization;
using System.Windows.Data;

namespace CtrlMonitor.App.Converters;

/// <summary>
/// Resta el parámetro al valor, con un mínimo. Sirve para que la vista de lista
/// ocupe el alto visible menos la cabecera de la página.
/// </summary>
public sealed class SubtractConverter : IValueConverter
{
    /// <summary>Alto mínimo del resultado, para ventanas muy chicas.</summary>
    public double Minimum { get; set; } = 320;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double number = value is null ? 0 : System.Convert.ToDouble(value, culture);
        double subtract = parameter is null ? 0 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);

        return Math.Max(Minimum, number - subtract);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
