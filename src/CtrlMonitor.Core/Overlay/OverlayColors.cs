namespace CtrlMonitor.Core.Overlay;

/// <summary>
/// Colores del overlay en formato <c>#AARRGGBB</c>. La configuración acepta
/// también <c>#RRGGBB</c> y lo normaliza antes de guardarlo.
/// </summary>
public static class OverlayColors
{
    /// <summary>Blanco opaco; se usa si la configuración trae un color inválido.</summary>
    public const string DefaultText = "#FFFFFFFF";

    /// <summary>Estilo minimalista: todo en un solo color, sin distracciones.</summary>
    public const string StyleMinimal = "minimal";

    /// <summary>Estilo variado: cada grupo (CPU, GPU, RAM, red) con su color.</summary>
    public const string StyleVaried = "varied";

    public const string DefaultStyle = StyleVaried;

    public static bool IsKnownStyle(string? style) =>
        string.Equals(style, StyleMinimal, StringComparison.OrdinalIgnoreCase)
        || string.Equals(style, StyleVaried, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Acepta <c>#RRGGBB</c> o <c>#AARRGGBB</c> (con o sin almohadilla) y
    /// devuelve siempre <c>#AARRGGBB</c> en mayúsculas.
    /// </summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = DefaultText;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string hex = value.Trim();

        if (!hex.StartsWith('#'))
            hex = "#" + hex;

        if (hex.Length == 7)
            hex = "#FF" + hex[1..];

        if (hex.Length != 9)
            return false;

        for (int i = 1; i < hex.Length; i++)
        {
            if (!Uri.IsHexDigit(hex[i]))
                return false;
        }

        normalized = hex.ToUpperInvariant();
        return true;
    }
}
