namespace CtrlMonitor.Core.Hardware;

/// <summary>Estado general de un disco para la interfaz.</summary>
public enum DiskCondition
{
    Unknown = 0,
    Good = 1,
    Caution = 2,
    Bad = 3
}

/// <summary>Un atributo S.M.A.R.T. tal como lo informa el disco.</summary>
/// <param name="Id">Identificador del atributo (0 en NVMe, que no usa IDs).</param>
/// <param name="Name">Nombre del atributo, en inglés, como en CrystalDiskInfo.</param>
/// <param name="Current">Valor normalizado actual (ATA); -1 si no aplica.</param>
/// <param name="Worst">Peor valor normalizado registrado (ATA); -1 si no aplica.</param>
/// <param name="Threshold">Umbral de falla (ATA); -1 si no aplica.</param>
/// <param name="Raw">Valor crudo.</param>
public sealed record SmartAttributeReading(int Id, string Name, int Current, int Worst, int Threshold, ulong Raw);

/// <summary>
/// Datos S.M.A.R.T. de un disco físico, leídos con el mismo motor que
/// CrystalDiskInfo (DiskInfoToolkit, incluido en LibreHardwareMonitor).
/// Cualquier valor puede faltar: depende del disco y de su interfaz.
/// </summary>
public sealed record DiskSmartInfo
{
    /// <summary>Número de disco de Windows («Disco 2»).</summary>
    public int Number { get; init; } = -1;

    public string Model { get; init; } = string.Empty;

    public string Firmware { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>«NVMe», «SATA», «USB»…</summary>
    public string Interface { get; init; } = string.Empty;

    public bool IsSsd { get; init; }

    public bool IsNvme { get; init; }

    public ulong SizeBytes { get; init; }

    /// <summary>Estado que calcula el motor de CrystalDiskInfo.</summary>
    public DiskCondition ToolkitStatus { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Temperatura de advertencia que declara el disco (NVMe), si la informa.</summary>
    public double? TemperatureWarning { get; init; }

    /// <summary>Vida útil restante en % (SSD).</summary>
    public double? LifePercent { get; init; }

    public double? HostReadsGb { get; init; }

    public double? HostWritesGb { get; init; }

    public ulong? PowerOnHours { get; init; }

    public ulong? PowerOnCount { get; init; }

    public ulong? UnsafeShutdowns { get; init; }

    public ulong? ReallocatedSectors { get; init; }

    public ulong? PendingSectors { get; init; }

    public ulong? UncorrectableSectors { get; init; }

    /// <summary>Errores de medio e integridad de datos (NVMe).</summary>
    public ulong? MediaErrors { get; init; }

    /// <summary>Reserva disponible en % (NVMe).</summary>
    public double? AvailableSpare { get; init; }

    /// <summary>Umbral de reserva disponible en % (NVMe).</summary>
    public double? AvailableSpareThreshold { get; init; }

    /// <summary>Advertencia crítica del controlador NVMe; distinto de 0 indica un problema.</summary>
    public ulong? CriticalWarning { get; init; }

    public IReadOnlyList<SmartAttributeReading> Attributes { get; init; } = Array.Empty<SmartAttributeReading>();
}

/// <summary>Un motivo por el que el estado del disco no es «Bueno».</summary>
/// <param name="Key">Clave de traducción del motivo.</param>
/// <param name="Value">Número que acompaña al motivo (sectores, %, etc.), si corresponde.</param>
public sealed record DiskIssue(string Key, double? Value = null);

/// <summary>
/// Calcula el estado general del disco combinando lo que dice Windows, el
/// criterio de CrystalDiskInfo y reglas propias sobre los atributos S.M.A.R.T.
/// Siempre gana el peor estado.
/// </summary>
public static class DiskHealthEvaluator
{
    /// <summary>Vida útil por debajo de la cual se avisa.</summary>
    public const double LowLifePercent = 10;

    /// <summary>Temperatura desde la que se avisa; lo ideal es estar por debajo.</summary>
    public const double HotTemperature = 50;

    public static (DiskCondition Condition, IReadOnlyList<DiskIssue> Issues) Evaluate(
        DiskSmartInfo smart,
        DiskCondition windowsStatus)
    {
        var issues = new List<DiskIssue>();
        DiskCondition worst = DiskCondition.Unknown;

        void Raise(DiskCondition condition, DiskIssue? issue = null)
        {
            if (condition > worst)
                worst = condition;

            if (issue is not null)
                issues.Add(issue);
        }

        if (windowsStatus != DiskCondition.Unknown)
        {
            Raise(windowsStatus, windowsStatus switch
            {
                DiskCondition.Caution => new DiskIssue("disks.issue.windowsWarning"),
                DiskCondition.Bad => new DiskIssue("disks.issue.windowsBad"),
                _ => null
            });
        }

        if (smart.ToolkitStatus != DiskCondition.Unknown)
            Raise(smart.ToolkitStatus);

        if (smart.CriticalWarning is > 0)
            Raise(DiskCondition.Bad, new DiskIssue("disks.issue.criticalWarning"));

        if (smart.AvailableSpare is { } spare && smart.AvailableSpareThreshold is { } threshold
            && threshold > 0 && spare < threshold)
        {
            Raise(DiskCondition.Bad, new DiskIssue("disks.issue.lowSpare", spare));
        }

        if (smart.ReallocatedSectors is > 0)
            Raise(DiskCondition.Caution, new DiskIssue("disks.issue.reallocated", smart.ReallocatedSectors));

        if (smart.PendingSectors is > 0)
            Raise(DiskCondition.Caution, new DiskIssue("disks.issue.pending", smart.PendingSectors));

        if (smart.UncorrectableSectors is > 0)
            Raise(DiskCondition.Caution, new DiskIssue("disks.issue.uncorrectable", smart.UncorrectableSectors));

        if (smart.MediaErrors is > 0)
            Raise(DiskCondition.Caution, new DiskIssue("disks.issue.mediaErrors", smart.MediaErrors));

        if (smart.LifePercent is { } life && life < LowLifePercent)
            Raise(DiskCondition.Caution, new DiskIssue("disks.issue.lowLife", life));

        // Si el motor de CrystalDiskInfo marca el disco y ninguna regla lo explica, se dice igual.
        if (smart.ToolkitStatus >= DiskCondition.Caution && issues.Count == 0)
            issues.Add(new DiskIssue("disks.issue.toolkit"));

        // Con datos S.M.A.R.T. pero sin señales de problema, el disco está bien.
        if (worst == DiskCondition.Unknown && (smart.Temperature is not null || smart.PowerOnHours is not null))
            worst = DiskCondition.Good;

        return (worst, issues);
    }
}
