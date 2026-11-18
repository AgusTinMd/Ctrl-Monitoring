using System.Diagnostics;
using System.Runtime.Versioning;

namespace CtrlMonitor.Core.Inventory;

/// <summary>Actividad de los discos físicos.</summary>
/// <param name="ActivePercent">
/// Tiempo activo del disco más ocupado, en %. Es la misma medida que muestra el
/// Administrador de tareas; se toma el máximo para que un disco al 100 % no
/// quede diluido por los que están quietos.
/// </param>
/// <param name="ReadBytesPerSecond">Lectura sumada de todos los discos.</param>
/// <param name="WriteBytesPerSecond">Escritura sumada de todos los discos.</param>
public readonly record struct DiskUsage(double? ActivePercent, double ReadBytesPerSecond, double WriteBytesPerSecond)
{
    private const double Megabyte = 1024d * 1024d;

    public double ReadMegaBytesPerSecond => ReadBytesPerSecond / Megabyte;

    public double WriteMegaBytesPerSecond => WriteBytesPerSecond / Megabyte;

    public static DiskUsage Unavailable => new(null, 0, 0);
}

/// <summary>
/// Lee la actividad de los discos con los contadores de rendimiento de Windows
/// (categoría PhysicalDisk). Abrir los contadores tarda varios segundos la
/// primera vez, así que se preparan en segundo plano y, mientras tanto,
/// <see cref="Read"/> devuelve <see cref="DiskUsage.Unavailable"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskUsageReader : IDisposable
{
    private const string Category = "PhysicalDisk";
    private const string TotalInstance = "_Total";
    private static readonly TimeSpan InstanceRefresh = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private Counters? _counters;
    private Task? _building;
    private DateTime _builtUtc = DateTime.MinValue;
    private bool _failed;
    private bool _disposed;

    public DiskUsage Read()
    {
        lock (_gate)
        {
            if (_failed || _disposed)
                return DiskUsage.Unavailable;

            // Los discos pueden conectarse o quitarse (USB): la lista se
            // rearma cada tanto, siempre fuera del hilo de la interfaz.
            if (_building is null && DateTime.UtcNow - _builtUtc >= InstanceRefresh)
                _building = Task.Run(Build);

            if (_counters is null)
                return DiskUsage.Unavailable;

            try
            {
                return _counters.Read();
            }
            catch (Exception ex) when (IsCounterFailure(ex))
            {
                Fail();
                return DiskUsage.Unavailable;
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
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            lock (_gate)
            {
                if (_counters is not null && instances.SequenceEqual(_counters.Instances))
                {
                    _builtUtc = DateTime.UtcNow;
                    _building = null;
                    return;
                }
            }

            var counters = new Counters(instances);

            lock (_gate)
            {
                if (_disposed)
                {
                    counters.Dispose();
                    return;
                }

                _counters?.Dispose();
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
        _counters?.Dispose();
        _counters = null;
    }

    private static bool IsCounterFailure(Exception ex) =>
        ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _counters?.Dispose();
            _counters = null;
        }
    }

    private sealed class Counters : IDisposable
    {
        private readonly List<PerformanceCounter> _idle = new();
        private readonly PerformanceCounter _read;
        private readonly PerformanceCounter _write;

        public Counters(string[] instances)
        {
            Instances = instances;
            _read = new PerformanceCounter(Category, "Disk Read Bytes/sec", TotalInstance, readOnly: true);
            _write = new PerformanceCounter(Category, "Disk Write Bytes/sec", TotalInstance, readOnly: true);

            // La primera lectura de un contador de tasa da 0; sin esta lectura
            // inicial el disco figuraría al 100 % de actividad.
            _read.NextValue();
            _write.NextValue();

            foreach (string instance in instances)
            {
                var counter = new PerformanceCounter(Category, "% Idle Time", instance, readOnly: true);
                counter.NextValue();
                _idle.Add(counter);
            }
        }

        public string[] Instances { get; }

        public DiskUsage Read()
        {
            double? active = null;

            foreach (PerformanceCounter idle in _idle)
            {
                double busy = Math.Clamp(100d - idle.NextValue(), 0, 100);
                active = active is { } current ? Math.Max(current, busy) : busy;
            }

            return new DiskUsage(
                active,
                Math.Max(0, _read.NextValue()),
                Math.Max(0, _write.NextValue()));
        }

        public void Dispose()
        {
            foreach (PerformanceCounter counter in _idle)
                counter.Dispose();

            _read.Dispose();
            _write.Dispose();
        }
    }
}
