using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Métrica que puede mostrar un panel del modo gráfico.</summary>
public sealed class ChartMetric
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required Brush Color { get; init; }

    public required string AxisUnit { get; init; }

    public required double AxisMin { get; init; }

    public required double AxisMax { get; init; }

    public required double AxisStep { get; init; }

    public required Func<MainViewModel, double?> Primary { get; init; }

    public required string PrimaryFormat { get; init; }

    public required string PrimaryUnit { get; init; }

    public string SecondaryLabel { get; init; } = string.Empty;

    public Func<MainViewModel, double?>? Secondary { get; init; }

    public string SecondaryFormat { get; init; } = "0";

    public string SecondaryUnit { get; init; } = string.Empty;

    /// <summary>Unidad dinámica; por ejemplo, el total de RAM junto a los GB en uso.</summary>
    public Func<MainViewModel, string>? SecondaryUnitSelector { get; init; }

    /// <summary>Color opcional del valor secundario, para diferenciarlo en el gráfico.</summary>
    public Brush? SecondaryBrush { get; init; }

    /// <summary>Etiqueta sobre el valor principal; por ejemplo, «BAJADA».</summary>
    public string PrimaryLabel { get; init; } = string.Empty;

    /// <summary>Unidad dinámica del valor principal.</summary>
    public Func<MainViewModel, string>? PrimaryUnitSelector { get; init; }

    public Func<MainViewModel, double?>? Tertiary { get; init; }

    public string TertiaryFormat { get; init; } = "0.0";

    public string TertiaryUnit { get; init; } = string.Empty;

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
        Options = options;
        _selectedMetric = selected;
        ApplyMetric();
    }

    public IReadOnlyList<ChartMetric> Options { get; }

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
    private bool _hasSecondary;

    [ObservableProperty]
    private bool _hasTertiary;

    /// <summary>La tarjeta se está arrastrando para cambiarla de lugar.</summary>
    [ObservableProperty]
    private bool _isDragging;

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
    }

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
        Title = "CPU",
        Subtitle = "Paquete del procesador",
        Color = UiBrushes.ChartCpu,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.CpuTemperature,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C",
        SecondaryLabel = "VENTILADOR",
        Secondary = vm => vm.CpuFanRpm,
        SecondaryFormat = "0",
        SecondaryUnit = " RPM",
        Tertiary = vm => vm.CpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric Gpu { get; } = new()
    {
        Key = "Gpu",
        Title = "GPU",
        Subtitle = "Núcleo de la tarjeta gráfica",
        Color = UiBrushes.ChartGpu,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.GpuTemperature,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C",
        SecondaryLabel = "VENTILADOR",
        Secondary = vm => vm.GpuFanRpm,
        SecondaryFormat = "0",
        SecondaryUnit = " RPM",
        Tertiary = vm => vm.GpuPower,
        TertiaryFormat = "0.0",
        TertiaryUnit = " W"
    };

    public static ChartMetric Board { get; } = new()
    {
        Key = "Board",
        Title = "Placa",
        Subtitle = "Sensor del Super I/O",
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
        Title = "Hotspot",
        Subtitle = "Punto caliente de la GPU",
        Color = UiBrushes.ChartHotspot,
        AxisUnit = "°",
        AxisMin = 20,
        AxisMax = 120,
        AxisStep = 10,
        Primary = vm => vm.GpuHotspot,
        PrimaryFormat = "0.0",
        PrimaryUnit = "°C",
        SecondaryLabel = "VENTILADOR",
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
        Title = "RAM",
        Subtitle = "Memoria física en uso",
        Color = UiBrushes.ChartRam,
        AxisUnit = "%",
        AxisMin = 0,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.RamPercent,
        PrimaryFormat = "0",
        PrimaryUnit = " %",
        SecondaryLabel = "EN USO",
        Secondary = vm => vm.RamUsedGb,
        SecondaryFormat = "0.0",
        SecondaryUnitSelector = vm => vm.RamTotalGb is { } total
            ? $" GB de {total.ToString("0.#", CultureInfo.CurrentCulture)} GB"
            : " GB"
    };

    public static ChartMetric Vram { get; } = new()
    {
        Key = "Vram",
        Title = "VRAM",
        Subtitle = "Memoria de la tarjeta gráfica",
        Color = UiBrushes.ChartVram,
        AxisUnit = "%",
        AxisMin = 0,
        AxisMax = 100,
        AxisStep = 10,
        Primary = vm => vm.VramPercent,
        PrimaryFormat = "0",
        PrimaryUnit = " %",
        SecondaryLabel = "EN USO",
        Secondary = vm => vm.VramUsedGb,
        SecondaryFormat = "0.0",
        SecondaryUnitSelector = vm => vm.VramTotalGb is { } total
            ? $" GB de {total.ToString("0.#", CultureInfo.CurrentCulture)} GB"
            : " GB"
    };

    public static ChartMetric Network { get; } = new()
    {
        Key = "Network",
        Title = "Red",
        Subtitle = "Bajada y subida de la red física",
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
        PrimaryLabel = "BAJADA",
        SecondaryLabel = "SUBIDA",
        Secondary = vm => vm.NetUpMbs,
        SecondaryFormat = "0.00",
        SecondaryUnit = " MB/s",
        SecondaryBrush = UiBrushes.ChartNetUp
    };

    public static IReadOnlyList<ChartMetric> All { get; } = new[]
    {
        Cpu,
        Gpu,
        Board,
        Hotspot,
        Ram,
        Vram,
        Network
    };
}
