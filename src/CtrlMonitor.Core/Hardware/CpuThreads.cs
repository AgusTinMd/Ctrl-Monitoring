using System.Text.RegularExpressions;

namespace CtrlMonitor.Core.Hardware;

/// <summary>Carga de un hilo del procesador (o de un núcleo, si no hay datos por hilo).</summary>
public sealed record CpuThreadLoad(int Core, int Thread, double Load);

/// <summary>
/// Saca la carga de cada hilo de los sensores del CPU. LibreHardwareMonitor
/// los publica como «CPU Core #3 Thread #2»; en procesadores o versiones sin
/// datos por hilo solo hay «CPU Core #3», y entonces se usa uno por núcleo.
/// </summary>
public static partial class CpuThreads
{
    [GeneratedRegex(@"^CPU Core #(\d+)(?:\s+Thread #(\d+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public static IReadOnlyList<CpuThreadLoad> From(IEnumerable<SensorReading> cpuSensors)
    {
        var threads = new List<CpuThreadLoad>();
        var cores = new List<CpuThreadLoad>();

        foreach (SensorReading sensor in cpuSensors)
        {
            if (sensor.Kind != SensorKind.Load)
                continue;

            Match match = NamePattern().Match(sensor.Name.Trim());

            if (!match.Success)
                continue;

            int core = int.Parse(match.Groups[1].Value);
            double load = Math.Clamp(sensor.Value, 0, 100);

            if (match.Groups[2].Success)
                threads.Add(new CpuThreadLoad(core, int.Parse(match.Groups[2].Value), load));
            else
                cores.Add(new CpuThreadLoad(core, 1, load));
        }

        List<CpuThreadLoad> result = threads.Count > 0 ? threads : cores;

        return result
            .GroupBy(t => (t.Core, t.Thread))
            .Select(g => g.First())
            .OrderBy(t => t.Core)
            .ThenBy(t => t.Thread)
            .ToList();
    }
}
