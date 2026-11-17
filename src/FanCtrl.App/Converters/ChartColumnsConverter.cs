using System.Globalization;
using System.Windows.Data;

namespace FanCtrl.App.Converters;

/// <summary>
/// Decide entre una o dos columnas para los gráficos. Prioriza el espacio
/// vertical: en un monitor en vertical conviene una columna, mientras que en
/// horizontal entran dos sin que los gráficos queden demasiado angostos.
/// </summary>
public sealed class ChartColumnsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2
            || values[0] is not double width
            || values[1] is not double height
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return 1;
        }

        bool wideEnough = width >= 720 && height >= 460;
        bool horizontal = width >= height * 1.1;

        return wideEnough && horizontal ? 2 : 1;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
