using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Configuration;

/// <summary>
/// Preferencias del overlay de métricas. Es una sección de <see cref="AppConfig"/>
/// para no romper las configuraciones anteriores: si el archivo no la trae, se
/// usan estos valores.
/// </summary>
public sealed class OverlayConfig
{
    /// <summary>Apagado por defecto: nadie ve una barra sin pedirlo.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Estilo de colores predefinido (<c>minimal</c> o <c>varied</c>). Se usa
    /// mientras <see cref="CustomColors"/> esté apagado.
    /// </summary>
    public string ColorStyle { get; set; } = OverlayColors.DefaultStyle;

    /// <summary>
    /// Apagado por defecto. Al activarlo, <see cref="TextColor"/> y
    /// <see cref="GroupColors"/> reemplazan al estilo predefinido.
    /// </summary>
    public bool CustomColors { get; set; }

    /// <summary>Color base del texto a medida, en <c>#AARRGGBB</c>.</summary>
    public string TextColor { get; set; } = OverlayColors.DefaultText;

    /// <summary>Tamaño del texto en píxeles (10 a 24).</summary>
    public double TextSize { get; set; } = 14;

    /// <summary>Opacidad del fondo negro, de 0 a 100.</summary>
    public double BackgroundOpacity { get; set; } = 60;

    /// <summary>Claves de las métricas visibles, en cualquier orden.</summary>
    public List<string> Metrics { get; set; } = OverlayMetricKeys.Defaults.ToList();

    /// <summary>
    /// Color por grupo (<c>cpu</c>, <c>gpu</c>, <c>ram</c>, <c>net</c>). Un
    /// valor vacío significa "usar el color global".
    /// </summary>
    public Dictionary<string, string> GroupColors { get; set; } =
        new(OverlayMetricKeys.DefaultGroupColors(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Posición guardada de la barra, en unidades de WPF. Null = arriba al centro.</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    /// <summary>
    /// Color final de un grupo: el estilo elegido o, con colores a medida, el
    /// color del grupo (o el color base si el grupo usa el global).
    /// </summary>
    public string ResolveGroupColor(string group)
    {
        if (CustomColors)
        {
            return GroupColors.TryGetValue(group, out string? custom) && !string.IsNullOrWhiteSpace(custom)
                ? custom
                : TextColor;
        }

        if (string.Equals(ColorStyle, OverlayColors.StyleMinimal, StringComparison.OrdinalIgnoreCase))
            return OverlayColors.DefaultText;

        return OverlayMetricKeys.DefaultGroupColors().TryGetValue(group, out string? styled)
            ? styled
            : OverlayColors.DefaultText;
    }

    public void Normalize()
    {
        ColorStyle = OverlayColors.IsKnownStyle(ColorStyle)
            ? ColorStyle.Trim().ToLowerInvariant()
            : OverlayColors.DefaultStyle;

        TextColor = OverlayColors.TryNormalize(TextColor, out string textColor)
            ? textColor
            : OverlayColors.DefaultText;

        TextSize = Math.Clamp(TextSize, 10, 24);
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100);

        // Si el archivo trae claves desconocidas se descartan. Si no queda
        // ninguna pero el usuario había elegido algo, se vuelve a lo inicial;
        // una lista vacía a propósito (todo desmarcado) se respeta.
        List<string> source = Metrics ?? new List<string>();
        List<string> metrics = source
            .Where(OverlayMetricKeys.IsKnown)
            .Select(key => key.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

        if (metrics.Count == 0 && source.Count > 0)
            metrics = OverlayMetricKeys.Defaults.ToList();

        Metrics = metrics;

        var rawColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (GroupColors is not null)
        {
            foreach ((string key, string value) in GroupColors)
                rawColors[key] = value;
        }

        IReadOnlyDictionary<string, string> defaults = OverlayMetricKeys.DefaultGroupColors();
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string group in OverlayMetricKeys.Groups)
        {
            if (rawColors.TryGetValue(group, out string? value) && string.IsNullOrWhiteSpace(value))
            {
                // Vacío es una elección válida: usar el color global.
                colors[group] = string.Empty;
            }
            else if (rawColors.TryGetValue(group, out value)
                     && OverlayColors.TryNormalize(value, out string normalized))
            {
                colors[group] = normalized;
            }
            else
            {
                colors[group] = defaults[group];
            }
        }

        GroupColors = colors;

        if (Left is { } left && !double.IsFinite(left))
            Left = null;

        if (Top is { } top && !double.IsFinite(top))
            Top = null;
    }
}
