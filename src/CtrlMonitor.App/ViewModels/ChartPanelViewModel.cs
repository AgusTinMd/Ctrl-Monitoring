using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>
/// Métrica que puede mostrar un panel del modo gráfico. Los textos se guardan
/// como claves de traducción; si una clave no existe (por ejemplo «CPU»), se
/// muestra tal cual. Avisa cuando cambia el idioma para que los selectores
/// actualicen los nombres.
/// </summary>
public sealed class ChartMetric : INotifyPropertyChanged
{
    public ChartMetric()
    {
        // Las métricas son estáticas y viven toda la app: no hace falta desuscribir.
        Loc.Instance.LanguageChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }

    /// <summary>Clave del nombre de la métrica.</summary>
    public required string TitleKey { get; init; }

    /// <summary>Clave de la descripción breve bajo el selector.</summary>
    public required string SubtitleKey { get; init; }

    /// <summary>Título armado en el momento; por ejemplo, «Disco 2 (C:)».</summary>
    public Func<string>? TitleSelector { get; init; }

    public string Title => TitleSelector?.Invoke() ?? Loc.T(TitleKey);

    public string Subtitle => Loc.T(SubtitleKey);

    public required Brush Color { get; init; }

    public required string AxisUnit { get; init; }

    public required double AxisMin { get; init; }

    public required double AxisMax { get; init; }

    public required double AxisStep { get; init; }

    public required Func<MainViewModel, double?> Primary { get; init; }

    public required string PrimaryFormat { get; init; }

    public required string PrimaryUnit { get; init; }

    /// <summary>Clave de la etiqueta sobre el valor secundario; por ejemplo, «VENTILADOR».</summary>
    public string SecondaryLabelKey { get; init; } = string.Empty;

    public string SecondaryLabel => SecondaryLabelKey.Length == 0 ? string.Empty : Loc.T(SecondaryLabelKey);

    public Func<MainViewModel, double?>? Secondary { get; init; }

    public string SecondaryFormat { get; init; } = "0";

    public string SecondaryUnit { get; init; } = string.Empty;

    /// <summary>Unidad dinámica; por ejemplo, el total de RAM junto a los GB en uso.</summary>
    public Func<MainViewModel, string>? SecondaryUnitSelector { get; init; }

    /// <summary>Color opcional del valor secundario, para diferenciarlo en el gráfico.</summary>
    public Brush? SecondaryBrush { get; init; }

    /// <summary>Clave de la etiqueta sobre el valor principal; por ejemplo, «BAJADA».</summary>
    public string PrimaryLabelKey { get; init; } = string.Empty;

    public string PrimaryLabel => PrimaryLabelKey.Length == 0 ? string.Empty : Loc.T(PrimaryLabelKey);

    /// <summary>Unidad dinámica del valor principal.</summary>
    public Func<MainViewModel, string>? PrimaryUnitSelector { get; init; }

    public Func<MainViewModel, double?>? Tertiary { get; init; }

    public string TertiaryFormat { get; init; } = "0.0";

    public string TertiaryUnit { get; init; } = string.Empty;

    /// <summary>Valor chico bajo el secundario; por ejemplo, los GHz del CPU.</summary>
    public Func<MainViewModel, double?>? Quaternary { get; init; }

    public string QuaternaryFormat { get; init; } = "0.00";

    public string QuaternaryUnit { get; init; } = string.Empty;

    public Brush? QuaternaryBrush { get; init; }

    public override string ToString() => Title;
}

/// <summary>
/// Una tarjeta del modo gráfico. Expone las métricas disponibles y los textos
/// ya formateados de la métrica elegida, de modo que el XAML sea una plantilla
/// única para todas las tarjetas.
/// </summary>
public sealed partial class ChartPanelViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public ChartPanelViewModel(
        MainViewModel owner,
        IReadOnlyList<ChartMetric> options,
        ChartMetric selected)
    {
        _owner = owner;
        _options = options;
        _selectedMetric = selected;
        ApplyMetric();
    }

    /// <summary>Métricas del selector; cambia cuando se detectan los discos.</summary>
    [ObservableProperty]
    private IReadOnlyList<ChartMetric> _options;

    [ObservableProperty]
    private ChartMetric _selectedMetric;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private Brush _color = Brushes.Gray;

    [ObservableProperty]
    private string _seriesKey = string.Empty;

    [ObservableProperty]
    private string _axisUnit = "°";

    [ObservableProperty]
    private double _axisMin;

    [ObservableProperty]
    private double _axisMax = 100;

    [ObservableProperty]
    private double _axisStep = 10;

    [ObservableProperty]
    private string _primaryValue = "—";

    [ObservableProperty]
    private string _primaryUnit = string.Empty;

    [ObservableProperty]
    private string _primaryLabel = string.Empty;

    [ObservableProperty]
    private bool _hasPrimaryLabel;

    [ObservableProperty]
    private string _secondaryLabel = string.Empty;

    [ObservableProperty]
    private string _secondaryValue = string.Empty;

    [ObservableProperty]
    private string _secondaryUnit = string.Empty;

    [ObservableProperty]
    private Brush _secondaryValueBrush = UiBrushes.TextSecondary;

    [ObservableProperty]
    private string _tertiaryValue = string.Empty;

    [ObservableProperty]
    private string _tertiaryUnit = string.Empty;

    [ObservableProperty]
    private string _quaternaryValue = string.Empty;

    [ObservableProperty]
    private string _quaternaryUnit = string.Empty;

    [ObservableProperty]
    private Brush _quaternaryValueBrush = UiBrushes.TextSecondary;

    [ObservableProperty]
    private bool _hasQuaternary;

    [ObservableProperty]
    private bool _hasSecondary;

    [ObservableProperty]
    private bool _hasTertiary;

    /// <summary>La tarjeta se está arrastrando para cambiarla de lugar.</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>
    /// Se hizo clic en la tarjeta y puede empezar a arrastrarse; se resalta
    /// para que quede claro cuál se va a mover.
    /// </summary>
    [ObservableProperty]
    private bool _isDragArmed;

    partial void OnSelectedMetricChanged(ChartMetric value) => ApplyMetric();

    /// <summary>Vuelve a leer los valores actuales del monitor sin cambiar la métrica.</summary>
    public void Refresh()
    {
        ChartMetric metric = SelectedMetric;

        PrimaryValue = Format(metric.Primary(_owner), metric.PrimaryFormat);
        PrimaryUnit = metric.PrimaryUnitSelector?.Invoke(_owner) ?? metric.PrimaryUnit;
        SecondaryValue = metric.Secondary is null
            ? string.Empty
            : Format(metric.Secondary(_owner), metric.SecondaryFormat);
        SecondaryUnit = metric.SecondaryUnitSelector?.Invoke(_owner) ?? metric.SecondaryUnit;
        TertiaryValue = metric.Tertiary is null
            ? string.Empty
            : Format(metric.Tertiary(_owner), metric.TertiaryFormat);
        QuaternaryValue = metric.Quaternary is null
            ? string.Empty
            : Format(metric.Quaternary(_owner), metric.QuaternaryFormat);
    }

    /// <summary>Vuelve a tomar los textos de la métrica después de cambiar de idioma.</summary>
    public void RefreshLanguage() => ApplyMetric();

    private void ApplyMetric()
    {
        ChartMetric metric = SelectedMetric;

        Subtitle = metric.Subtitle;
        Color = metric.Color;
        SeriesKey = metric.Key;
        AxisUnit = metric.AxisUnit;
        AxisMin = metric.AxisMin;
        AxisMax = metric.AxisMax;
        AxisStep = metric.AxisStep;
        PrimaryUnit = metric.PrimaryUnit;
        PrimaryLabel = metric.PrimaryLabel;
        HasPrimaryLabel = metric.PrimaryLabel.Length > 0;
        SecondaryLabel = metric.SecondaryLabel;
        SecondaryUnit = metric.SecondaryUnit;
        SecondaryValueBrush = metric.SecondaryBrush ?? UiBrushes.TextSecondary;
        TertiaryUnit = metric.TertiaryUnit;
        HasSecondary = metric.Secondary is not null;
        HasTertiary = metric.Tertiary is not null;
        QuaternaryUnit = metric.QuaternaryUnit;
        QuaternaryValueBrush = metric.QuaternaryBrush ?? UiBrushes.TextSecondary;
        HasQuaternary = metric.Quaternary is not null;

        Refresh();
    }

    private static string Format(double? value, string format) =>
        value is { } number
            ? number.ToString(format, CultureInfo.CurrentCulture)
            : "—";
}

/// <summary>Catálogo de métricas disponibles en el modo gráfico.</summary>
internal static class ChartMetrics
{
    public static ChartMetric Cpu { get; } = new()
    {
        Key = "Cpu",
        TitleKey = "CPU",
        SubtitleKey = "metric.cpu.subtitle",
        Color = UiBrushes.ChartCpu,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.CpuTemperature,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C",
        SecondaryLabelKey = "metric.label.fan",
        Secondary = vm => vm.CpuFanRpm,
        SecondaryFormat = "0",
        SecondaryUnit = " RPM",
        Quaternary = vm => vm.CpuClockGhz,
        QuaternaryUnit = " GHz",
        Tertiary = vm => vm.CpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric Gpu { get; } = new()
    {
        Key = "Gpu",
        TitleKey = "GPU",
        SubtitleKey = "metric.gpu.subtitle",
        Color = UiBrushes.ChartGpu,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.GpuTemperature,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C",
        SecondaryLabelKey = "metric.label.fan",
        Secondary = vm => vm.GpuFanRpm,
        SecondaryFormat = "0",
        SecondaryUnit = " RPM",
        Tertiary = vm => vm.GpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric CpuLoad { get; } = new()
    {
        Key = "CpuLoad",
        TitleKey = "metric.cpuLoad.title",
        SubtitleKey = "metric.cpuLoad.subtitle",
        Color = UiBrushes.ChartCpu,
        AxisUnit = "%",
        AxisMin = 0,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.CpuLoad,
        PrimaryFormat = "0",
        PrimaryUnit = " %",
        SecondaryLabelKey = "metric.label.temp",
        Secondary = vm => vm.CpuTemperature,
        SecondaryFormat = "0.0",
        SecondaryUnit = " °C",
        Quaternary = vm => vm.CpuClockGhz,
        QuaternaryUnit = " GHz",
        Tertiary = vm => vm.CpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric GpuLoad { get; } = new()
    {
        Key = "GpuLoad",
        TitleKey = "metric.gpuLoad.title",
        SubtitleKey = "metric.gpuLoad.subtitle",
        Color = UiBrushes.ChartGpu,
        AxisUnit = "%",
        AxisMin = 0,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.GpuLoad,
        PrimaryFormat = "0",
        PrimaryUnit = " %",
        SecondaryLabelKey = "metric.label.temp",
        Secondary = vm => vm.GpuTemperature,
        SecondaryFormat = "0.0",
        SecondaryUnit = " °C",
        Tertiary = vm => vm.GpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric Board { get; } = new()
    {
        Key = "Board",
        TitleKey = "metric.board.title",
        SubtitleKey = "metric.board.subtitle",
        Color = UiBrushes.ChartBoard,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.BoardTemperature,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C"
    };

    public static ChartMetric Hotspot { get; } = new()
    {
        Key = "Hotspot",
        TitleKey = "Hotspot",
        SubtitleKey = "metric.hotspot.subtitle",
        Color = UiBrushes.ChartHotspot,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 120,
        AxisStep = 10,
        Primary = vm => vm.GpuHotspot,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C",
        SecondaryLabelKey = "metric.label.fan",
        Secondary = vm => vm.GpuFanRpm,
        SecondaryFormat = "0",
        SecondaryUnit = " RPM",
        Tertiary = vm => vm.GpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric Ram { get; } = new()
    {
        Key = "Ram",
        TitleKey = "RAM",
        SubtitleKey = "metric.ram.subtitle",
        Color = UiBrushes.ChartRam,
        AxisUnit = "%",
        AxisMin = 0,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.RamPercent,
        PrimaryFormat = "0",
        PrimaryUnit = " %",
        SecondaryLabelKey = "metric.label.inUse",
        Secondary = vm => vm.RamUsedGb,
        SecondaryFormat = "0.0",
        SecondaryUnitSelector = vm => vm.RamTotalGb is { } total
            ? Loc.F("unit.gbOf", total.ToString("0.#", CultureInfo.CurrentCulture))
            : " GB"
    };

    public static ChartMetric Vram { get; } = new()
    {
        Key = "Vram",
        TitleKey = "VRAM",
        SubtitleKey = "metric.vram.subtitle",
        Color = UiBrushes.ChartVram,
        AxisUnit = "%",
        AxisMin = 0,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.VramPercent,
        PrimaryFormat = "0",
        PrimaryUnit = " %",
        SecondaryLabelKey = "metric.label.inUse",
        Secondary = vm => vm.VramUsedGb,
        SecondaryFormat = "0.0",
        SecondaryUnitSelector = vm => vm.VramTotalGb is { } total
            ? Loc.F("unit.gbOf", total.ToString("0.#", CultureInfo.CurrentCulture))
            : " GB"
    };

    public static ChartMetric Network { get; } = new()
    {
        Key = "Network",
        TitleKey = "metric.network.title",
        SubtitleKey = "metric.network.subtitle",
        Color = UiBrushes.ChartNetDown,
        AxisUnit = " MB/s",
        AxisMin = 0,
        // Techo amplio: 1 Gbps ronda 119 MB/s y 10 Gbps, 1190 MB/s. Con 100
        // una descarga a velocidad plena quedaba recortada contra el borde.
        AxisMax = 1250,
        AxisStep = 1,
        Primary = vm => vm.NetDownMbs,
        PrimaryFormat = "0.00",
        PrimaryUnit = " MB/s",
        PrimaryLabelKey = "metric.label.down",
        SecondaryLabelKey = "metric.label.up",
        Secondary = vm => vm.NetUpMbs,
        SecondaryFormat = "0.00",
        SecondaryUnit = " MB/s",
        SecondaryBrush = UiBrushes.ChartNetUp
    };

    private static readonly Dictionary<string, ChartMetric> DiskMetrics = new(StringComparer.Ordinal);

    /// <summary>Prefijo de las claves de disco: «Disk:2 C:».</summary>
    public const string DiskKeyPrefix = "Disk:";

    /// <summary>
    /// Métrica de un disco físico. Se cachea por disco para que el selector
    /// reconozca la misma instancia al refrescar la lista de discos.
    /// </summary>
    public static ChartMetric ForDisk(string diskId)
    {
        if (DiskMetrics.TryGetValue(diskId, out ChartMetric? cached))
            return cached;

        int number = DiskNames.Number(diskId);
        string letters = DiskNames.Letters(diskId);

        var metric = new ChartMetric
        {
            Key = DiskKeyPrefix + diskId,
            TitleKey = "metric.disk.title",
            TitleSelector = () => letters.Length > 0
                ? Loc.F("metric.disk.titleLetters", number, letters)
                : Loc.F("metric.disk.title", number),
            SubtitleKey = "metric.disk.subtitle",
            Color = UiBrushes.ChartDiskRead,
            AxisUnit = " MB/s",
            AxisMin = 0,
            // Techo para un NVMe rápido; la escala automática lo ajusta al tráfico real.
            AxisMax = 8000,
            AxisStep = 1,
            Primary = vm => vm.GetDisk(diskId)?.ActivePercent,
            PrimaryFormat = "0",
            PrimaryUnit = " %",
            PrimaryLabelKey = "metric.label.active",
            SecondaryLabelKey = "metric.label.readWrite",
            Secondary = vm => vm.GetDisk(diskId)?.ReadMegaBytesPerSecond,
            SecondaryFormat = "0.0",
            SecondaryUnit = " MB/s",
            SecondaryBrush = UiBrushes.ChartDiskRead,
            Quaternary = vm => vm.GetDisk(diskId)?.WriteMegaBytesPerSecond,
            QuaternaryFormat = "0.0",
            QuaternaryUnit = " MB/s",
            QuaternaryBrush = UiBrushes.ChartDiskWrite
        };

        DiskMetrics[diskId] = metric;
        return metric;
    }

    /// <summary>Métricas fijas; las de cada disco se agregan cuando se detectan.</summary>
    public static IReadOnlyList<ChartMetric> Base { get; } = new[]
    {
        Cpu,
        Gpu,
        CpuLoad,
        GpuLoad,
        Board,
        Hotspot,
        Ram,
        Vram,
        Network
    };
}
