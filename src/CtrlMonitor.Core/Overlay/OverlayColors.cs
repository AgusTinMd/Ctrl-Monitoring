namespace CtrlMonitor.Core.Overlay;

/// <summary>
/// Colores del overlay en formato <c>#AARRGGBB</c>. La configuración acepta
/// también <c>#RRGGBB</c> y lo normaliza antes de guardarlo.
/// </summary>
public static class OverlayColors
{
    /// <summary>Blanco opaco; se usa si la configuración trae un color inválido.</summary>
    public const string DefaultText = "#FFFFFFFF";

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
