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

    /// <summary>Ícono de Windows con la temperatura, para la bandeja.</summary>
    public static System.Drawing.Icon RenderIcon(double celsius)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(celsius)));

        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        using var bitmap = new System.Drawing.Bitmap(stream);
        IntPtr handle = bitmap.GetHicon();

        try
        {
            // Copia propia: el handle de GetHicon se libera enseguida.
            using var temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>El logo de la app, para volver a él al apagar la opción.</summary>
    public static System.Drawing.Icon DefaultIcon()
    {
        System.Windows.Resources.StreamResourceInfo? resource =
            Application.GetResourceStream(new Uri("pack://application:,,,/Assets/fan.ico"));

        using System.IO.Stream stream = resource?.Stream ?? throw new InvalidOperationException("Falta el ícono de la app.");
        return new System.Drawing.Icon(stream);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
