using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>
/// Un disco físico en la tarjeta de almacenamiento de Inicio: salud,
/// temperatura y desgaste, con sus unidades debajo.
/// </summary>
public sealed partial class StorageDiskViewModel : ObservableObject
{
    /// <summary>Temperatura desde la que se marca en advertencia.</summary>
    private const double HotTemperature = 55;

    private readonly PhysicalDiskInfo? _info;

    public StorageDiskViewModel(PhysicalDiskInfo? info, IEnumerable<VolumeViewModel> volumes)
    {
        _info = info;
        Volumes = new ObservableCollection<VolumeViewModel>(volumes);
        RefreshLanguage();
    }

    /// <summary>Número de disco, o -1 para el grupo de unidades sin disco reconocido.</summary>
    public int Number => _info?.Number ?? -1;

    /// <summary>Modelo, para cruzarlo con los sensores de LibreHardwareMonitor.</summary>
    public string Model => _info?.Model ?? string.Empty;

    public ObservableCollection<VolumeViewModel> Volumes { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string _healthText = string.Empty;

    [ObservableProperty]
    private Brush _healthBrush = UiBrushes.Muted;

    [ObservableProperty]
    private bool _hasHealth;

    [ObservableProperty]
    private string _temperatureText = string.Empty;

    [ObservableProperty]
    private Brush _temperatureBrush = UiBrushes.TextSecondary;

    [ObservableProperty]
    private bool _hasTemperature;

    [ObservableProperty]
    private string _lifeText = string.Empty;

    [ObservableProperty]
    private bool _hasLife;

    private double? _temperature;
    private double? _life;

    /// <summary>Muestras de lectura y escritura (MB/s) para el mini gráfico.</summary>
    private const int ActivitySamples = 40;

    private readonly List<double> _readHistory = new();
    private readonly List<double> _writeHistory = new();
    private double _readMbs;
    private double _writeMbs;

    [ObservableProperty]
    private IReadOnlyList<double> _readValues = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _writeValues = Array.Empty<double>();

    [ObservableProperty]
    private bool _hasActivity;

    [ObservableProperty]
    private string _readText = string.Empty;

    [ObservableProperty]
    private string _writeText = string.Empty;

    /// <summary>Lectura y escritura actuales, en MB/s.</summary>
    public void UpdateActivity(double readMbs, double writeMbs)
    {
        _readMbs = Math.Max(0, readMbs);
        _writeMbs = Math.Max(0, writeMbs);

        Push(_readHistory, _readMbs);
        Push(_writeHistory, _writeMbs);

        // Copias nuevas: el gráfico se redibuja al cambiar la referencia.
        ReadValues = _readHistory.ToArray();
        WriteValues = _writeHistory.ToArray();
        HasActivity = true;
        RefreshActivityText();
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);

        if (history.Count > ActivitySamples)
            history.RemoveAt(0);
    }

    private void RefreshActivityText()
    {
        ReadText = Loc.F("storage.read", FormatSpeed(_readMbs));
        WriteText = Loc.F("storage.write", FormatSpeed(_writeMbs));
    }

    private static string FormatSpeed(double mbs) =>
        mbs >= 100 ? mbs.ToString("0", CultureInfo.CurrentCulture) + " MB/s"
        : mbs >= 1 ? mbs.ToString("0.0", CultureInfo.CurrentCulture) + " MB/s"
        : (mbs * 1024).ToString("0", CultureInfo.CurrentCulture) + " KB/s";

    /// <summary>
    /// Toma la temperatura y el desgaste de los sensores del disco. NVMe
    /// informa «Percentage Used» (desgaste usado); algunos SATA, «Remaining Life».
    /// </summary>
    public void UpdateSensors(IReadOnlyList<SensorReading> sensors)
    {
        _temperature = sensors.FirstOrDefault(s => s.Kind == SensorKind.Temperature && s.Value > 0)?.Value;

        SensorReading? used = sensors.FirstOrDefault(s =>
            s.Kind == SensorKind.Level && s.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase));
        SensorReading? remaining = sensors.FirstOrDefault(s =>
            s.Kind == SensorKind.Level && s.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase));

        _life = remaining?.Value ?? (used is null ? null : 100 - used.Value);

        ApplySensorTexts();
    }

    public void RefreshLanguage()
    {
        if (HasActivity)
            RefreshActivityText();

        Title = _info is null ? Loc.T("storage.otherDrives") : Loc.F("storage.disk", _info.Number);
        Detail = _info is null ? string.Empty : BuildDetail(_info);

        (HealthText, HealthBrush, HasHealth) = _info?.Health switch
        {
            DiskHealth.Healthy => (Loc.T("storage.health.healthy"), UiBrushes.Success, true),
            DiskHealth.Warning => (Loc.T("storage.health.warning"), UiBrushes.Warning, true),
            DiskHealth.Unhealthy => (Loc.T("storage.health.unhealthy"), UiBrushes.Danger, true),
            DiskHealth.Unknown => (Loc.T("storage.health.unknown"), UiBrushes.Muted, true),
            _ => (string.Empty, UiBrushes.Muted, false)
        };

        foreach (VolumeViewModel volume in Volumes)
            volume.RefreshLanguage();

        ApplySensorTexts();
    }

    private void ApplySensorTexts()
    {
        HasTemperature = _temperature is not null;
        TemperatureText = _temperature is { } t ? t.ToString("0", CultureInfo.CurrentCulture) + " °C" : string.Empty;
        TemperatureBrush = _temperature >= HotTemperature ? UiBrushes.Warning : UiBrushes.TextSecondary;

        HasLife = _life is not null;
        // Un espacio y no vacío: el renglón ocupa su alto y alinea los discos entre sí.
        LifeText = _life is { } life ? Loc.F("storage.life", Math.Clamp(life, 0, 100)) : " ";
    }

    private static string BuildDetail(PhysicalDiskInfo info)
    {
        var parts = new List<string>();

        if (info.Model.Length > 0)
            parts.Add(info.Model);

        string kind = string.Join(" ", new[] { info.MediaType, info.BusType }.Where(part => part.Length > 0));

        if (kind.Length > 0)
            parts.Add(kind);

        if (info.SizeBytes > 0)
            parts.Add(FormatSize(info.SizeBytes));

        return string.Join(" · ", parts);
    }

    // Los fabricantes miden en potencias de 1000: «500 GB», «1 TB».
    private static string FormatSize(long bytes) =>
        bytes >= 1_000_000_000_000
            ? $"{bytes / 1e12:0.#} TB"
            : $"{bytes / 1e9:0} GB";
}
