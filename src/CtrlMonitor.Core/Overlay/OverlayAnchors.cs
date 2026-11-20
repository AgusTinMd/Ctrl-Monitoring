namespace CtrlMonitor.Core.Overlay;

/// <summary>Rectángulo simple, sin depender de WPF, para calcular posiciones.</summary>
public readonly record struct OverlayRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// Posiciones predefinidas de la barra. Con un ancla, la barra queda pegada a
/// ese borde aunque cambie de ancho (por ejemplo, al sumar métricas, una barra
/// arriba a la derecha crece hacia la izquierda).
/// </summary>
public static class OverlayAnchors
{
    /// <summary>Posición libre, elegida arrastrando la barra.</summary>
    public const string Custom = "custom";

    public const string TopLeft = "top-left";
    public const string TopCenter = "top-center";
    public const string TopRight = "top-right";
    public const string MiddleLeft = "middle-left";
    public const string MiddleCenter = "middle-center";
    public const string MiddleRight = "middle-right";
    public const string BottomLeft = "bottom-left";
    public const string BottomCenter = "bottom-center";
    public const string BottomRight = "bottom-right";

    public const string Default = TopCenter;

    /// <summary>Las nueve posiciones, en orden de lectura (fila por fila).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        TopLeft, TopCenter, TopRight,
        MiddleLeft, MiddleCenter, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    ];

    public static bool IsKnown(string? anchor) =>
        string.Equals(anchor, Custom, StringComparison.OrdinalIgnoreCase)
        || All.Any(key => string.Equals(key, anchor, StringComparison.OrdinalIgnoreCase));

    /// <summary>Esquina superior izquierda de la barra para un ancla dentro del área.</summary>
    public static (double Left, double Top) Place(string anchor, OverlayRect area, double width, double height)
    {
        int index = IndexOf(anchor);

        if (index < 0)
            index = IndexOf(Default);

        int column = index % 3;
        int row = index / 3;

        double left = column switch
        {
            0 => area.Left,
            1 => area.Left + ((area.Width - width) / 2),
            _ => area.Right - width
        };

        double top = row switch
        {
            0 => area.Top,
            1 => area.Top + ((area.Height - height) / 2),
            _ => area.Bottom - height
        };

        return (Math.Round(left), Math.Round(top));
    }

    /// <summary>
    /// Imán al soltar la barra: si queda cerca de un borde o del centro, se
    /// alinea. Si se alinea en los dos ejes, devuelve el ancla correspondiente;
    /// si no, <see cref="Custom"/> con la posición (alineada en el eje que se pudo).
    /// </summary>
    public static string Snap(
        OverlayRect area,
        double left,
        double top,
        double width,
        double height,
        double threshold,
        out double snappedLeft,
        out double snappedTop)
    {
        int column = SnapAxis(left, width, area.Left, area.Width, threshold, out snappedLeft);
        int row = SnapAxis(top, height, area.Top, area.Height, threshold, out snappedTop);

        return column >= 0 && row >= 0 ? All[(row * 3) + column] : Custom;
    }

    private static int SnapAxis(double start, double size, double areaStart, double areaSize, double threshold, out double snapped)
    {
        double[] targets =
        [
            areaStart,
            areaStart + ((areaSize - size) / 2),
            areaStart + areaSize - size
        ];

        int best = -1;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < targets.Length; i++)
        {
            double distance = Math.Abs(start - targets[i]);

            if (distance <= threshold && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        snapped = best >= 0 ? Math.Round(targets[best]) : start;
        return best;
    }

    private static int IndexOf(string? anchor)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i], anchor, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
