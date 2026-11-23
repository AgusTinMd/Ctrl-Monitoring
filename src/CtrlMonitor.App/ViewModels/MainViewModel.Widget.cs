using System.Globalization;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Widget compacto de escritorio (opcional, desactivado por defecto).</summary>
public partial class MainViewModel
{
    private bool _widgetShown;

    /// <summary>Mostrar el widget de escritorio.</summary>
    public bool ShowWidget
    {
        get => _config.WidgetEnabled;
        set
        {
            if (_config.WidgetEnabled == value)
                return;

            _config.WidgetEnabled = value;
            OnPropertyChanged();
            SaveSoon();
        }
    }

    /// <summary>El widget queda encima de las demás ventanas.</summary>
    public bool WidgetTopmost
    {
        get => _config.WidgetTopmost;
        set
        {
            if (_config.WidgetTopmost == value)
                return;

            _config.WidgetTopmost = value;
            OnPropertyChanged();
            SaveSoon();
        }
    }

    public double? WidgetLeft => _config.WidgetLeft;

    public double? WidgetTop => _config.WidgetTop;

    public void SetWidgetPosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
            return;

        _config.WidgetLeft = left;
        _config.WidgetTop = top;
        SaveSoon();
    }

    /// <summary>Bajada y subida de red para el widget.</summary>
    public string WidgetNetText =>
        NetDownMbs is { } down && NetUpMbs is { } up
            ? Loc.F("widget.netValue", down.ToString("0.0", CultureInfo.CurrentCulture), up.ToString("0.0", CultureInfo.CurrentCulture))
            : "—";

    /// <summary>Con el widget a la vista, la app sigue midiendo aunque la ventana esté oculta.</summary>
    public void SetWidgetShown(bool shown)
    {
        _widgetShown = shown;
        UpdateActivity();
    }

    partial void OnNetDownMbsChanged(double? value) => OnPropertyChanged(nameof(WidgetNetText));

    partial void OnNetUpMbsChanged(double? value) => OnPropertyChanged(nameof(WidgetNetText));
}
