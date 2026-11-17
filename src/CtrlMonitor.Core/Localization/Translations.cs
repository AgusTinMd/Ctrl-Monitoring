namespace CtrlMonitor.Core.Localization;

/// <summary>
/// Diccionarios de textos por idioma. Cada idioma vive en su propio archivo
/// (<c>Translations.Es.cs</c>, <c>Translations.En.cs</c>, <c>Translations.PtBr.cs</c>)
/// y todos deben tener las mismas claves; un test lo verifica.
/// </summary>
public static partial class Translations
{
    // Diferido a propósito: los diccionarios de cada idioma están en otros
    // archivos de esta clase partial y el orden de inicialización de campos
    // estáticos entre archivos no está garantizado.
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _byLanguage;

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByLanguage =>
        _byLanguage ??= new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["es"] = Es,
            ["en"] = En,
            ["pt-BR"] = PtBr
        };

    /// <summary>Textos del idioma pedido; inglés si no está soportado.</summary>
    public static IReadOnlyDictionary<string, string> For(string code) =>
        ByLanguage.TryGetValue(code, out IReadOnlyDictionary<string, string>? texts)
            ? texts
            : En;

    /// <summary>Todos los idiomas con sus textos, para los tests de completitud.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> All => ByLanguage;
}
