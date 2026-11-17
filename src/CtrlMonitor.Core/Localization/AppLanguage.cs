using System.Globalization;

namespace CtrlMonitor.Core.Localization;

/// <summary>Idioma que la app puede mostrar.</summary>
/// <param name="Code">Código de cultura: «es», «en» o «pt-BR».</param>
/// <param name="NativeName">Nombre del idioma escrito en ese idioma.</param>
public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// Idiomas soportados y cómo se elige el idioma cuando la configuración está
/// en «automático».
/// </summary>
public static class AppLanguage
{
    /// <summary>Valor de configuración que sigue el idioma de Windows.</summary>
    public const string Auto = "auto";

    /// <summary>Idioma de respaldo: se usa si Windows está en un idioma no soportado.</summary>
    public const string Fallback = "en";

    public static IReadOnlyList<LanguageInfo> Supported { get; } = new[]
    {
        new LanguageInfo("es", "Español"),
        new LanguageInfo("en", "English"),
        new LanguageInfo("pt-BR", "Português (Brasil)")
    };

    /// <summary>
    /// Idioma de la interfaz de Windows al arrancar, antes de que la app cambie
    /// <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public static CultureInfo SystemUICulture { get; } = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Deja la preferencia guardada en un valor válido: «auto» o uno de los
    /// códigos soportados, sin distinguir mayúsculas.
    /// </summary>
    public static string NormalizeSetting(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
            return Auto;

        string trimmed = setting.Trim();

        LanguageInfo? match = Supported.FirstOrDefault(language =>
            language.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase));

        return match?.Code ?? Auto;
    }

    /// <summary>
    /// Idioma a mostrar según la preferencia. Con «auto» usa el idioma de
    /// <paramref name="systemCulture"/>: español y portugués se respetan
    /// (cualquier variante), el resto cae en inglés.
    /// </summary>
    public static string Resolve(string? setting, CultureInfo? systemCulture = null)
    {
        string normalized = NormalizeSetting(setting);

        if (normalized != Auto)
            return normalized;

        string language = (systemCulture ?? SystemUICulture).TwoLetterISOLanguageName;

        return language switch
        {
            "es" => "es",
            "pt" => "pt-BR",
            _ => Fallback
        };
    }
}
