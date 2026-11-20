namespace CtrlMonitor.Core.Overlay;

/// <summary>
/// Claves estables de las métricas del overlay. Se guardan en la configuración,
/// así que no conviene renombrarlas aunque cambie la interfaz.
/// </summary>
public static class OverlayMetricKeys
{
    public const string CpuGroup = "cpu";
    public const string GpuGroup = "gpu";
    public const string RamGroup = "ram";
    public const string NetGroup = "net";

    public const string CpuLoad = "cpu.load";
    public const string CpuTemperature = "cpu.temp";
    public const string CpuClock = "cpu.clock";
    public const string CpuPower = "cpu.power";
    public const string CpuFan = "cpu.fan";

    public const string GpuLoad = "gpu.load";
    public const string GpuTemperature = "gpu.temp";
    public const string GpuHotspot = "gpu.hotspot";
    public const string GpuPower = "gpu.power";
    public const string GpuFan = "gpu.fan";
    public const string GpuVram = "gpu.vram";

    public const string RamUsed = "ram.used";
    public const string RamPercent = "ram.percent";

    public const string NetThroughput = "net.throughput";

    /// <summary>Todas las métricas disponibles, en el orden del selector.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CpuLoad, CpuTemperature, CpuClock, CpuPower, CpuFan,
        GpuLoad, GpuTemperature, GpuHotspot, GpuVram, GpuPower, GpuFan,
        RamUsed, RamPercent,
        NetThroughput
    };

    /// <summary>Selección inicial: lo que muestra la barra al activarla.</summary>
    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        CpuLoad, CpuTemperature, CpuClock,
        GpuLoad, GpuTemperature, GpuVram,
        RamUsed
    };

    /// <summary>Grupos con color propio.</summary>
    public static readonly IReadOnlyList<string> Groups = new[]
    {
        CpuGroup, GpuGroup, RamGroup, NetGroup
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Known.Contains(key.Trim());

    /// <summary>Grupo al que pertenece una clave; vacío si no se reconoce.</summary>
    public static string GroupOf(string key)
    {
        if (key.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase))
            return CpuGroup;

        if (key.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase))
            return GpuGroup;

        if (key.StartsWith("ram.", StringComparison.OrdinalIgnoreCase))
            return RamGroup;

        if (key.StartsWith("net.", StringComparison.OrdinalIgnoreCase))
            return NetGroup;

        return string.Empty;
    }

    public static bool IsCpu(string key) => GroupOf(key) == CpuGroup;
    public static bool IsGpu(string key) => GroupOf(key) == GpuGroup;
    public static bool IsRam(string key) => GroupOf(key) == RamGroup;
    public static bool IsNet(string key) => GroupOf(key) == NetGroup;

    /// <summary>
    /// Colores iniciales por grupo, al estilo de la barra de NVIDIA: CPU
    /// amarillo, GPU verde, RAM violeta y red cian.
    /// </summary>
    public static Dictionary<string, string> DefaultGroupColors() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [CpuGroup] = "#FFFFD166",
            [GpuGroup] = "#FF7BD88F",
            [RamGroup] = "#FFC792EA",
            [NetGroup] = "#FF56B6C2"
        };
}
