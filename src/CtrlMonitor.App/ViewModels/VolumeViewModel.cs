using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Una unidad en la tarjeta de almacenamiento de Inicio.</summary>
public sealed partial class VolumeViewModel : ObservableObject
{
    /// <summary>A partir de este % de uso la barra se pinta de advertencia.</summary>
    private const double WarningPercent = 90;

    public VolumeViewModel(VolumeUsage volume)
    {
        Letter = volume.Letter;
        Update(volume);
    }

    public string Letter { get; }

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private double _usedPercent;

    [ObservableProperty]
    private string _usedText = string.Empty;

    [ObservableProperty]
    private string _freeText = string.Empty;

    [ObservableProperty]
    private Brush _barBrush = UiBrushes.Accent;

    private VolumeUsage? _last;

    public void Update(VolumeUsage volume)
    {
        _last = volume;
        Label = volume.Label;
        UsedPercent = volume.UsedPercent;
        UsedText = Loc.F("storage.used", FormatBytes(volume.UsedBytes), volume.UsedPercent);
        FreeText = Loc.F("storage.free", FormatBytes(volume.FreeBytes), FormatBytes(volume.TotalBytes));
        BarBrush = volume.UsedPercent >= WarningPercent ? UiBrushes.Warning : UiBrushes.Accent;
    }

    /// <summary>Vuelve a traducir los textos después de cambiar de idioma.</summary>
    public void RefreshLanguage()
    {
        if (_last is not null)
            Update(_last);
    }

    private static string FormatBytes(long bytes)
    {
        const double Gigabyte = 1024d * 1024d * 1024d;
        const double Terabyte = Gigabyte * 1024d;

        return bytes >= Terabyte
            ? $"{bytes / Terabyte:0.##} TB"
            : $"{bytes / Gigabyte:0.#} GB";
    }
}
