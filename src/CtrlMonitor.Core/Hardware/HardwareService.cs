using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace CtrlMonitor.Core.Hardware;

/// <summary>
/// Capa única sobre LibreHardwareMonitor. Serializa todas las lecturas
/// (el Super I/O no es thread-safe) y publica snapshots inmutables.
/// Esta versión es de solo lectura: nunca escribe PWM ni modifica el BIOS.
/// </summary>
public sealed class HardwareService : IDisposable
{
    private readonly Computer _computer;
    private readonly object _gate = new();

    private Timer? _timer;
    private int _tickInProgress;
    private volatile bool _disposed;
    private HardwareSnapshot _latest = HardwareSnapshot.Empty;

    public HardwareService()
    {
        _computer = new Computer(new InMemorySettings())
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsMemoryEnabled = false,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsPowerMonitorEnabled = false
        };
    }

    public string MotherboardName { get; private set; } = "Placa madre";

    public string CpuName { get; private set; } = "CPU";

    public string GpuName { get; private set; } = "GPU";

    public string SuperIoName { get; private set; } = "Super I/O";

    public string DriverVersion => PawnIoInstalled
        ? LibreHardwareMonitor.PawnIo.PawnIo.Version?.ToString() ?? "instalado"
        : "no instalado";

    public bool PawnIoInstalled => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;

    public bool IsAdministrator
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public HardwareSnapshot Latest
    {
        get
        {
            lock (_gate)
                return _latest;
        }
    }

    public string? LastError { get; private set; }

    public event EventHandler<HardwareSnapshot>? SnapshotUpdated;

    public void Open()
    {
        _computer.Open();

        lock (_gate)
        {
            UpdateAllLocked();
            DiscoverNamesLocked();
            _latest = BuildSnapshotLocked();
        }

        SnapshotUpdated?.Invoke(this, _latest);
    }

    public void StartMonitoring(TimeSpan interval)
    {
        if (_disposed)
            return;

        _timer?.Dispose();
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, interval);
    }

    public void StopMonitoring()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        if (_disposed || Interlocked.Exchange(ref _tickInProgress, 1) == 1)
            return;

        try
        {
            HardwareSnapshot snapshot;

            lock (_gate)
            {
                // Dispose puede haber cerrado el Computer mientras esperábamos
                // el lock: leer sensores cerrados toca el Super I/O sin driver.
                if (_disposed)
                    return;

                UpdateAllLocked();
                snapshot = BuildSnapshotLocked();
                _latest = snapshot;
            }

            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            Volatile.Write(ref _tickInProgress, 0);
        }
    }

    /// <summary>
    /// Pide leer los discos (temperatura, desgaste). Consultar S.M.A.R.T. en
    /// cada refresco es costo innecesario, así que solo se hace mientras la
    /// interfaz los muestra y como mucho cada <see cref="StorageInterval"/>.
    /// </summary>
    public bool StorageUpdatesEnabled
    {
        get => Volatile.Read(ref _storageUpdatesEnabled);
        set => Volatile.Write(ref _storageUpdatesEnabled, value);
    }

    private static readonly TimeSpan StorageInterval = TimeSpan.FromSeconds(5);
    private bool _storageUpdatesEnabled;
    private DateTime _storageUpdatedUtc = DateTime.MinValue;

    private void UpdateAllLocked()
    {
        bool updateStorage = _storageUpdatedUtc == DateTime.MinValue
                             || (StorageUpdatesEnabled && DateTime.UtcNow - _storageUpdatedUtc >= StorageInterval);

        if (updateStorage)
            _storageUpdatedUtc = DateTime.UtcNow;

        foreach (IHardware hardware in Flatten(_computer.Hardware))
        {
            if (hardware.HardwareType == HardwareType.Storage && !updateStorage)
                continue;

            try
            {
                hardware.Update();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    private void DiscoverNamesLocked()
    {
        List<IHardware> all = Flatten(_computer.Hardware).ToList();

        MotherboardName = all.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard)?.Name
                          ?? "Placa madre";

        CpuName = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?.Name
                 ?? "CPU";

        GpuName = all.FirstOrDefault(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia)?.Name
                 ?? "GPU";

        SuperIoName = all.FirstOrDefault(h => h.HardwareType == HardwareType.SuperIO)?.Name
                      ?? "Super I/O";
    }

    private HardwareSnapshot BuildSnapshotLocked()
    {
        var readings = new List<SensorReading>();

        foreach (IHardware hardware in Flatten(_computer.Hardware))
        {
            string hardwareId = hardware.Identifier.ToString();
            string hardwareType = hardware.HardwareType.ToString();

            foreach (ISensor sensor in hardware.Sensors)
            {
                SensorKind? kind = MapKind(sensor.SensorType);

                if (kind is null || sensor.Value is null)
                    continue;

                string name = kind == SensorKind.Pwm
                    ? $"{sensor.Name} · PWM"
                    : sensor.Name;

                readings.Add(new SensorReading(
                    sensor.Identifier.ToString(),
                    hardwareId,
                    hardware.Name,
                    hardwareType,
                    name,
                    kind.Value,
                    sensor.Index,
                    sensor.Value.Value,
                    UnitFor(kind.Value)));
            }
        }

        readings.Sort(CompareReadings);

        return new HardwareSnapshot(DateTime.UtcNow, readings);
    }

    private static int CompareReadings(SensorReading a, SensorReading b)
    {
        int byHardwareType = HardwareOrder(a.HardwareType).CompareTo(HardwareOrder(b.HardwareType));
        if (byHardwareType != 0)
            return byHardwareType;

        int byHardware = string.Compare(a.HardwareId, b.HardwareId, StringComparison.Ordinal);
        if (byHardware != 0)
            return byHardware;

        int byKind = a.Kind.CompareTo(b.Kind);
        if (byKind != 0)
            return byKind;

        int byIndex = a.Index.CompareTo(b.Index);
        if (byIndex != 0)
            return byIndex;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int HardwareOrder(string hardwareType) => hardwareType switch
    {
        "Cpu" => 0,
        "GpuAmd" or "GpuNvidia" => 1,
        "Motherboard" => 2,
        "SuperIO" => 3,
        _ => 4
    };

    private static SensorKind? MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Load => SensorKind.Load,
        SensorType.Control => SensorKind.Pwm,
        SensorType.SmallData => SensorKind.Memory,
        SensorType.Level => SensorKind.Level,
        SensorType.Clock => SensorKind.Clock,
        _ => null
    };

    private static string UnitFor(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Fan => "RPM",
        SensorKind.Power => "W",
        SensorKind.Voltage => "V",
        SensorKind.Load or SensorKind.Pwm or SensorKind.Level => "%",
        SensorKind.Memory => "MB",
        SensorKind.Clock => "MHz",
        _ => string.Empty
    };

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (IHardware item in hardware)
        {
            yield return item;

            foreach (IHardware sub in Flatten(item.SubHardware))
                yield return sub;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopMonitoring();

        // Timer.Dispose no espera al callback en curso. Tomar el mismo lock
        // que Tick garantiza que ninguna lectura esté usando el hardware
        // mientras se cierra, y Tick revisa _disposed antes de volver a leer.
        lock (_gate)
        {
            try
            {
                _computer.Close();
            }
            catch
            {
                // best effort
            }
        }
    }
}
