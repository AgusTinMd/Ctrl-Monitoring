using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CtrlMonitor.Core.Processes;

/// <summary>Una lectura de un proceso para el administrador de procesos.</summary>
/// <param name="Id">PID.</param>
/// <param name="StartTicks">
/// Momento de inicio (ticks UTC) o 0 si no se pudo leer. Junto con el PID
/// identifica al proceso aunque Windows reutilice el número.
/// </param>
/// <param name="Name">Nombre del ejecutable sin «.exe».</param>
/// <param name="Path">Ruta completa del ejecutable, si se pudo leer.</param>
/// <param name="CpuPercent">% de CPU de todo el equipo; <c>null</c> en la primera lectura.</param>
/// <param name="MemoryBytes">Memoria en uso (conjunto de trabajo privado).</param>
public sealed record ProcessSample(int Id, long StartTicks, string Name, string? Path, double? CpuPercent, long MemoryBytes)
{
    /// <summary>Clave estable del proceso: PID más momento de inicio.</summary>
    public (int, long) Key => (Id, StartTicks);
}

/// <summary>
/// Lee los procesos en ejecución con su uso de CPU y memoria. El % de CPU se
/// calcula con la diferencia de tiempo de procesador entre dos lecturas, así
/// que está pensado para llamarse a intervalos regulares.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessSampler
{
    private readonly Dictionary<(int, long), TimeSpan> _lastCpu = new();
    private long _lastTimestamp;

    public IReadOnlyList<ProcessSample> Read()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = _lastTimestamp == 0 ? 0 : (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;

        double totalCapacity = elapsedSeconds * Environment.ProcessorCount;
        var currentCpu = new Dictionary<(int, long), TimeSpan>();
        var result = new List<ProcessSample>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    int id = process.Id;
                    long start = TryGet(() => process.StartTime.ToUniversalTime().Ticks);
                    (int, long) key = (id, start);

                    double? cpu = null;
                    TimeSpan? total = TryGetNullable(() => process.TotalProcessorTime);

                    if (total is { } cpuTime)
                    {
                        currentCpu[key] = cpuTime;

                        if (totalCapacity > 0 && _lastCpu.TryGetValue(key, out TimeSpan previous))
                            cpu = Math.Clamp((cpuTime - previous).TotalSeconds / totalCapacity * 100, 0, 100);
                    }

                    long memory = NativeProcess.PrivateWorkingSet(id) ?? TryGet(() => process.WorkingSet64);

                    result.Add(new ProcessSample(
                        id,
                        start,
                        id == 0 ? "Idle" : process.ProcessName,
                        NativeProcess.ImagePath(id),
                        cpu,
                        memory));
                }
                catch (InvalidOperationException)
                {
                    // El proceso terminó mientras se leía.
                }
            }
        }

        _lastCpu.Clear();

        foreach ((var key, TimeSpan value) in currentCpu)
            _lastCpu[key] = value;

        return result;
    }

    private static long TryGet(Func<long> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return 0;
        }
    }

    private static TimeSpan? TryGetNullable(Func<TimeSpan> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Llamadas nativas con permisos limitados, como las usa el Administrador de tareas.</summary>
[SupportedOSPlatform("windows")]
internal static class NativeProcess
{
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>Ruta del ejecutable; funciona también con muchos procesos protegidos.</summary>
    public static string? ImagePath(int id)
    {
        if (id <= 4)
            return null;

        IntPtr handle = OpenProcess(QueryLimitedInformation, false, id);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Conjunto de trabajo privado: la columna «Memoria» del Administrador de
    /// tareas. Se calcula con la memoria privada confirmada como aproximación.
    /// </summary>
    public static long? PrivateWorkingSet(int id)
    {
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, id);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var counters = new ProcessMemoryCountersEx { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
            return GetProcessMemoryInfo(handle, ref counters, counters.Size)
                ? (long)Math.Min(counters.PrivateUsage, counters.WorkingSetSize)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSizeRaw;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsageRaw;

        public ulong WorkingSetSize => WorkingSetSizeRaw.ToUInt64();

        public ulong PrivateUsage => PrivateUsageRaw.ToUInt64();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx counters, uint size);
}
