using System.Windows.Data;
using System.Windows.Markup;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.Localization;

/// <summary>
/// Texto traducido en XAML: <c>Text="{l:Tr summary.subtitle}"</c>. Es un
/// binding al indexador de <see cref="Loc"/>, así que se actualiza solo al
/// cambiar de idioma desde Ajustes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
