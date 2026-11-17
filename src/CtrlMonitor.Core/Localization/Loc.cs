using System.ComponentModel;
using System.Globalization;

namespace CtrlMonitor.Core.Localization;

/// <summary>
/// Textos traducidos de la interfaz. La UI se enlaza a <see cref="Instance"/>
/// con su indexador (<c>Loc.Instance["clave"]</c>): al cambiar de idioma se
/// notifica <c>Item[]</c> y todos los textos se actualizan sin reiniciar.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, string> _current = Translations.For(AppLanguage.Fallback);

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    /// <summary>Código del idioma activo («es», «en» o «pt-BR»).</summary>
    public string Language { get; private set; } = AppLanguage.Fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara después de cambiar de idioma.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Texto traducido; si falta, el de inglés; si tampoco está, la clave.</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_current.TryGetValue(key, out string? value))
            return value;

        return Translations.For(AppLanguage.Fallback).TryGetValue(key, out string? fallback)
            ? fallback
            : key;
    }

    /// <summary>Atajo de <see cref="Get"/> sobre la instancia única.</summary>
    public static string T(string key) => Instance.Get(key);

    /// <summary>Texto traducido con formato, usando la cultura regional de Windows para los números.</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Instance.Get(key), args);

    /// <summary>
    /// Cambia el idioma de la interfaz. Solo cambia <see cref="CultureInfo.CurrentUICulture"/>:
    /// los números y fechas siguen el formato regional de Windows.
    /// </summary>
    public void SetLanguage(string code)
    {
        string resolved = AppLanguage.Resolve(code);

        if (resolved == Language && ReferenceEquals(_current, Translations.For(resolved)))
            return;

        Language = resolved;
        _current = Translations.For(resolved);

        var culture = CultureInfo.GetCultureInfo(resolved);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
