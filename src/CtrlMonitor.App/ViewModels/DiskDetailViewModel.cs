using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Inventory;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App.ViewModels;

/// <summary>Un dato de la ficha de un disco: «Horas de encendido · 12.345 h».</summary>
public sealed partial class DiskStatViewModel : ObservableObject
{
    public DiskStatViewModel(string key) => Key = key;

    public string Key { get; }

    [ObservableProperty]
    private string _caption = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private Brush _valueBrush = UiBrushes.TextPrimary;
}

/// <summary>Una fila de la tabla de atributos S.M.A.R.T.</summary>
public sealed record SmartAttributeRow(string Id, string Name, string Current, string Worst, string Threshold, string Raw);

/// <summary>Ficha completa de un disco en la sección Discos.</summary>
public sealed partial class DiskDetailViewModel : ObservableObject
{
    public DiskDetailViewModel(int number) => Number = number;

    public int Number { get; }

    /// <summary>La ficha muestra sus datos; al contraerla queda solo el encabezado.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<DiskStatViewModel> Stats { get; } = new();

    public ObservableCollection<string> Issues { get; } = new();

    public ObservableCollection<SmartAttributeRow> Attributes { get; } = new();

    /// <summary>Unidades del disco, con su espacio usado y libre.</summary>
    public ObservableCollection<VolumeViewModel> Volumes { get; } = new();

    [ObservableProperty]
    private bool _hasVolumes;

    /// <summary>Reemplaza las unidades del disco (se leen al entrar a la sección).</summary>
    public void SetVolumes(IEnumerable<VolumeUsage> volumes)
    {
        Volumes.Clear();

        foreach (VolumeUsage volume in volumes)
            Volumes.Add(new VolumeViewModel(volume));

        HasVolumes = Volumes.Count > 0;
    }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Logo de la marca del disco, si hay uno en Assets.</summary>
    [ObservableProperty]
    private string? _logoUri;

    [ObservableProperty]
    private bool _hasLogo;

    /// <summary>Marca reconocida sin logo (Kingston, Crucial…): se muestra como insignia.</summary>
    [ObservableProperty]
    private string? _brandText;

    [ObservableProperty]
    private bool _hasBrandText;

    [ObservableProperty]
    private string _conditionText = string.Empty;

    [ObservableProperty]
    private Brush _conditionBrush = UiBrushes.Muted;

    [ObservableProperty]
    private Brush _conditionSoftBrush = UiBrushes.NeutralSoft;

    [ObservableProperty]
    private string _temperatureText = "—";

    [ObservableProperty]
    private Brush _temperatureBrush = UiBrushes.TextPrimary;

    [ObservableProperty]
    private bool _hasIssues;

    [ObservableProperty]
    private bool _hasAttributes;

    [ObservableProperty]
    private string _attributesHeader = string.Empty;

    private DiskSmartInfo? _smart;
    private DiskCondition _windows;

    public void Update(DiskSmartInfo smart, DiskCondition windowsStatus)
    {
        _smart = smart;
        _windows = windowsStatus;
        Apply();
    }

    /// <summary>Vuelve a armar los textos después de cambiar de idioma.</summary>
    public void RefreshLanguage()
    {
        Apply();

        foreach (VolumeViewModel volume in Volumes)
            volume.RefreshLanguage();
    }

    private void Apply()
    {
        if (_smart is not { } smart)
            return;

        CultureInfo culture = CultureInfo.CurrentCulture;
        (DiskCondition condition, IReadOnlyList<DiskIssue> issues) = DiskHealthEvaluator.Evaluate(smart, _windows);

        Title = Loc.F("storage.disk", smart.Number);
        Model = smart.Model;

        HardwareBrand? brand = DiskBrands.Detect(smart.Model);
        LogoUri = brand?.LogoFile is { } file ? $"pack://application:,,,/Assets/{file}" : null;
        HasLogo = LogoUri is not null;
        BrandText = brand is { LogoFile: null } ? brand.Name.ToUpperInvariant() : null;
        HasBrandText = BrandText is not null;
        Subtitle = string.Join(" · ", new[]
        {
            string.Join(" ", new[] { smart.IsSsd ? "SSD" : "HDD", smart.Interface }.Where(part => part.Length > 0)),
            FormatSize(smart.SizeBytes)
        }.Where(part => part.Length > 0));

        (ConditionText, ConditionBrush, ConditionSoftBrush) = condition switch
        {
            DiskCondition.Good => (Loc.T("disks.condition.good"), UiBrushes.Success, UiBrushes.SuccessSoft),
            DiskCondition.Caution => (Loc.T("disks.condition.caution"), UiBrushes.Warning, UiBrushes.WarningSoft),
            DiskCondition.Bad => (Loc.T("disks.condition.bad"), UiBrushes.Danger, UiBrushes.DangerSoft),
            _ => (Loc.T("disks.condition.unknown"), UiBrushes.Muted, UiBrushes.NeutralSoft)
        };

        double hot = smart.TemperatureWarning ?? DiskHealthEvaluator.HotTemperature;
        TemperatureText = smart.Temperature is { } t ? t.ToString("0", culture) + " °C" : "—";
        TemperatureBrush = smart.Temperature >= DiskHealthEvaluator.HotTemperature ? UiBrushes.Warning : UiBrushes.TextPrimary;

        Issues.Clear();

        foreach (DiskIssue issue in issues)
            Issues.Add(issue.Value is { } value ? Loc.F(issue.Key, value) : Loc.T(issue.Key));

        HasIssues = Issues.Count > 0;

        var stats = new List<(string Key, string Value, string Note, Brush? Brush)>
        {
            ("disks.stat.temperature", TemperatureText, Loc.F("disks.note.idealTemperature", DiskHealthEvaluator.HotTemperature),
                smart.Temperature >= hot ? UiBrushes.Warning : null)
        };

        if (smart.LifePercent is { } life)
            stats.Add(("disks.stat.life", life.ToString("0", culture) + " %", string.Empty,
                life < DiskHealthEvaluator.LowLifePercent ? UiBrushes.Warning : null));

        if (smart.PowerOnHours is { } hours)
            stats.Add(("disks.stat.powerOnHours", hours.ToString("N0", culture) + " h",
                Loc.F("disks.note.days", hours / 24d), null));

        if (smart.PowerOnCount is { } count)
            stats.Add(("disks.stat.powerOnCount", count.ToString("N0", culture), string.Empty, null));

        if (smart.UnsafeShutdowns is { } unsafeShutdowns)
            stats.Add(("disks.stat.unsafeShutdowns", unsafeShutdowns.ToString("N0", culture), string.Empty, null));

        if (smart.HostWritesGb is { } written)
            stats.Add(("disks.stat.written", FormatGigabytes(written), string.Empty, null));

        if (smart.HostReadsGb is { } read)
            stats.Add(("disks.stat.read", FormatGigabytes(read), string.Empty, null));

        AddCounter(stats, "disks.stat.reallocated", smart.ReallocatedSectors);
        AddCounter(stats, "disks.stat.pending", smart.PendingSectors);
        AddCounter(stats, "disks.stat.uncorrectable", smart.UncorrectableSectors);
        AddCounter(stats, "disks.stat.mediaErrors", smart.MediaErrors);

        if (smart.AvailableSpare is { } spare)
        {
            string note = smart.AvailableSpareThreshold is { } threshold
                ? Loc.F("disks.note.spareThreshold", threshold)
                : string.Empty;
            stats.Add(("disks.stat.availableSpare", spare.ToString("0", culture) + " %", note,
                smart.AvailableSpareThreshold is { } limit && spare < limit ? UiBrushes.Danger : null));
        }

        if (smart.Interface.Length > 0)
            stats.Add(("disks.stat.interface", smart.Interface, string.Empty, null));

        if (smart.Firmware.Length > 0)
            stats.Add(("disks.stat.firmware", smart.Firmware, string.Empty, null));

        if (smart.SerialNumber.Length > 0)
            stats.Add(("disks.stat.serial", smart.SerialNumber, string.Empty, null));

        ApplyStats(stats);

        // Los atributos cambian poco: solo se rearma la tabla si cambió algo.
        List<SmartAttributeRow> rows = smart.Attributes.Select(ToRow).ToList();

        if (!rows.SequenceEqual(Attributes))
        {
            Attributes.Clear();

            foreach (SmartAttributeRow row in rows)
                Attributes.Add(row);
        }

        HasAttributes = Attributes.Count > 0;
        AttributesHeader = Loc.F("disks.attributes", Attributes.Count);
    }

    private static void AddCounter(List<(string, string, string, Brush?)> stats, string key, ulong? value)
    {
        if (value is { } number)
            stats.Add((key, number.ToString("N0", CultureInfo.CurrentCulture), string.Empty, number > 0 ? UiBrushes.Warning : null));
    }

    /// <summary>Actualiza los datos en el lugar para que la ficha no parpadee.</summary>
    private void ApplyStats(List<(string Key, string Value, string Note, Brush? Brush)> stats)
    {
        if (!stats.Select(s => s.Key).SequenceEqual(Stats.Select(s => s.Key)))
        {
            Stats.Clear();

            foreach ((string key, _, _, _) in stats)
                Stats.Add(new DiskStatViewModel(key));
        }

        for (int i = 0; i < stats.Count; i++)
        {
            DiskStatViewModel stat = Stats[i];
            stat.Caption = Loc.T(stats[i].Key);
            stat.Value = stats[i].Value;
            stat.Note = stats[i].Note;
            stat.ValueBrush = stats[i].Brush ?? UiBrushes.TextPrimary;
        }
    }

    private static SmartAttributeRow ToRow(SmartAttributeReading attribute) => new(
        attribute.Id > 0 ? attribute.Id.ToString("X2", CultureInfo.InvariantCulture) : "—",
        attribute.Name,
        attribute.Current >= 0 ? attribute.Current.ToString(CultureInfo.CurrentCulture) : "—",
        attribute.Worst >= 0 ? attribute.Worst.ToString(CultureInfo.CurrentCulture) : "—",
        attribute.Threshold >= 0 ? attribute.Threshold.ToString(CultureInfo.CurrentCulture) : "—",
        attribute.Raw.ToString("N0", CultureInfo.CurrentCulture));

    private static string FormatGigabytes(double gigabytes) =>
        gigabytes >= 1000
            ? (gigabytes / 1000d).ToString("0.##", CultureInfo.CurrentCulture) + " TB"
            : gigabytes.ToString("0", CultureInfo.CurrentCulture) + " GB";

    // Los fabricantes miden en potencias de 1000: «500 GB», «1 TB».
    private static string FormatSize(ulong bytes) =>
        bytes == 0 ? string.Empty
        : bytes >= 1_000_000_000_000 ? $"{bytes / 1e12:0.#} TB"
        : $"{bytes / 1e9:0} GB";
}
