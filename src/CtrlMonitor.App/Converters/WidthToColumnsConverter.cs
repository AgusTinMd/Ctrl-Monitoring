using System.Globalization;
using System.Windows.Data;

namespace CtrlMonitor.App.Converters;

/// <summary>
/// Calcula columnas a partir del ancho disponible. El parámetro acepta
/// umbrales con el formato «700:1,1100:2,3»: por debajo de 700 una columna,
/// por debajo de 1100 dos y en el resto tres.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return 1;

        double width;

        try
        {
            width = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return 1;
        }

        if (double.IsNaN(width) || double.IsInfinity(width))
            return 1;

        int fallback = 1;
        string spec = parameter as string ?? string.Empty;

        foreach (string rawPart in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = rawPart.IndexOf(':');

            if (colon < 0)
            {
                if (int.TryParse(rawPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain))
                    fallback = plain;

                continue;
            }

            string thresholdText = rawPart[..colon];
            string columnsText = rawPart[(colon + 1)..];

            if (double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold)
                && int.TryParse(columnsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int columns)
                && width < threshold)
            {
                return Math.Max(1, columns);
            }
        }

        return Math.Max(1, fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
