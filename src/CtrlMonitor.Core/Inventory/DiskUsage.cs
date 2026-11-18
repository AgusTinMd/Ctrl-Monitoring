using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CtrlMonitor.Core.Inventory;

/// <summary>Actividad de un disco físico.</summary>
/// <param name="Id">
/// Instancia del contador PhysicalDisk, por ejemplo «2 C:»: número de disco y
/// letras de sus volúmenes. Sirve de identificador estable del disco.
/// </param>
/// <param name="ActivePercent">Tiempo activo, en %; lo mismo que muestra el Administrador de tareas.</param>
/// <param name="ReadBytesPerSecond">Velocidad de lectura.</param>
/// <param name="WriteBytesPerSecond">Velocidad de escritura.</param>
public sealed record DiskActivity(string Id, double ActivePercent, double ReadBytesPerSecond, double WriteBytesPerSecond)
{
    private const double Megabyte = 1024d * 1024d;

    public double ReadMegaBytesPerSecond => ReadBytesPerSecond / Megabyte;

    public double WriteMegaBytesPerSecond => WriteBytesPerSecond / Megabyte;

    /// <summary>Número de disco de Windows (el «2» de «2 C:»), o -1 si no se reconoce.</summary>
    public int Number => DiskNames.Number(Id);

    /// <summary>Letras de los volúmenes, por ejemplo «C:» o «D: E:»; vacío si no tiene.</summary>
    public string Letters => DiskNames.Letters(Id);
}

/// <summary>Interpreta los nombres de instancia de PhysicalDisk («0 D:», «1 E: F:», «3»).</summary>
public static partial class DiskNames
{
    [GeneratedRegex(@"^\s*(\d+)\s*(.*)$")]
    private static partial Regex InstancePattern();

    public static int Number(string id)
    {
        Match match = InstancePattern().Match(id);
        return match.Success && int.TryParse(match.Groups[1].Value, out int number) ? number : -1;
    }

    public static string Letters(string id)
    {
        Match match = InstancePattern().Match(id);
        return match.Success ? match.Groups[2].Value.Trim() : id.Trim();
    }
}

/// <summary>
/// Lee la actividad de cada disco físico con los contadores de rendimiento de
/// Windows (categoría PhysicalDisk). Abrir los contadores tarda varios segundos
/// la primera vez, así que se preparan en segundo plano y, mientras tanto,
/// <see cref="Read"/> devuelve una lista vacía.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskUsageReader : IDisposable
{
    private const string Category = "PhysicalDisk";
    private const string TotalInstance = "_Total";
    private static readonly TimeSpan InstanceRefresh = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private List<DiskCounters>? _counters;
    private Task? _building;
    private DateTime _builtUtc = DateTime.MinValue;
    private bool _failed;
    private bool _disposed;

    /// <summary>Actividad de cada disco, ordenada por número de disco.</summary>
    public IReadOnlyList<DiskActivity> Read()
    {
        lock (_gate)
        {
            if (_failed || _disposed)
                return Array.Empty<DiskActivity>();

            // Los discos pueden conectarse o quitarse (USB): la lista se
            // rearma cada tanto, siempre fuera del hilo de la interfaz.
            if (_building is null && DateTime.UtcNow - _builtUtc >= InstanceRefresh)
                _building = Task.Run(Build);

            if (_counters is null)
                return Array.Empty<DiskActivity>();

            try
            {
                return _counters.Select(counter => counter.Read()).ToList();
            }
            catch (Exception ex) when (IsCounterFailure(ex))
            {
                Fail();
                return Array.Empty<DiskActivity>();
            }
        }
    }

    private void Build()
    {
        try
        {
            string[] instances = new PerformanceCounterCategory(Category)
                .GetInstanceNames()
                .Where(name => !name.Equals(TotalInstance, StringComparison.OrdinalIgnoreCase))
                .OrderBy(DiskNames.Number)
                .ThenBy(name => name, StringComparer.Ordinal)
                .ToArray();

            lock (_gate)
            {
                if (_counters is not null && instances.SequenceEqual(_counters.Select(c => c.Id)))
                {
                    _builtUtc = DateTime.UtcNow;
                    _building = null;
                    return;
                }
            }

            List<DiskCounters> counters = instances.Select(id => new DiskCounters(id)).ToList();

            lock (_gate)
            {
                if (_disposed)
                {
                    counters.ForEach(counter => counter.Dispose());
                    return;
                }

                _counters?.ForEach(counter => counter.Dispose());
                _counters = counters;
                _builtUtc = DateTime.UtcNow;
                _building = null;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (IsCounterFailure(ex))
                {
                    // Contadores deshabilitados o dañados en este equipo: se deja de intentar.
                    Fail();
                }
                else
                {
                    // Error pasajero: se reintenta en el próximo ciclo de revisión.
                    _building = null;
                    _builtUtc = DateTime.UtcNow;
                }
            }
        }
    }

    private void Fail()
    {
        _failed = true;
        _building = null;
        _counters?.ForEach(counter => counter.Dispose());
        _counters = null;
    }

    private static bool IsCounterFailure(Exception ex) =>
        ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _counters?.ForEach(counter => counter.Dispose());
            _counters = null;
        }
    }

    private sealed class DiskCounters : IDisposable
    {
        private readonly PerformanceCounter _idle;
        private readonly PerformanceCounter _read;
        private readonly PerformanceCounter _write;

        public DiskCounters(string id)
        {
            Id = id;
            _idle = new PerformanceCounter(Category, "% Idle Time", id, readOnly: true);
            _read = new PerformanceCounter(Category, "Disk Read Bytes/sec", id, readOnly: true);
            _write = new PerformanceCounter(Category, "Disk Write Bytes/sec", id, readOnly: true);

            // La primera lectura de un contador de tasa da 0; sin esta lectura
            // inicial el disco figuraría al 100 % de actividad.
            _idle.NextValue();
            _read.NextValue();
            _write.NextValue();
        }

        public string Id { get; }

        public DiskActivity Read() => new(
            Id,
            Math.Clamp(100d - _idle.NextValue(), 0, 100),
            Math.Max(0, _read.NextValue()),
            Math.Max(0, _write.NextValue()));

        public void Dispose()
        {
            _idle.Dispose();
            _read.Dispose();
            _write.Dispose();
        }
    }
}
