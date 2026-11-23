using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Dibuja el ícono de la bandeja con la temperatura: el número sobre un
/// cuadrado oscuro, del color de la escala térmica (frío → crítico).
/// </summary>
internal static class TrayTemperatureIcon
{
    private const int Size = 32;

    private static readonly Typeface Font = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Brush Background = Frozen("#0D0F12");

    /// <summary>Mismos colores que la escala térmica del tema.</summary>
    public static Brush ColorFor(double celsius) => celsius switch
    {
        < 40 => Frozen("#5AB8F5"),
        < 60 => Frozen("#67D6B0"),
        < 75 => Frozen("#F4C152"),
        < 88 => Frozen("#FF8A3D"),
        _ => Frozen("#FF4D6D")
    };

    /// <summary>Clave para no redibujar si no cambió nada visible.</summary>
    public static string Key(double celsius) =>
        Math.Round(celsius).ToString("0", CultureInfo.InvariantCulture) + "|" + ((SolidColorBrush)ColorFor(celsius)).Color;

    public static BitmapSource Render(double celsius)
    {
        string text = Math.Clamp(Math.Round(celsius), 0, 199).ToString("0", CultureInfo.InvariantCulture);
        double fontSize = text.Length >= 3 ? 15 : 21;

        var formatted = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, fontSize,
            ColorFor(celsius), 1.0);

        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, Size, Size), 5, 5);
            dc.DrawText(formatted, new Point((Size - formatted.Width) / 2, (Size - formatted.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
