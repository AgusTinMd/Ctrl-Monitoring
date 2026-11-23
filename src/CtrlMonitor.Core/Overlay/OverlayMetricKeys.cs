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
    public const string FpsGroup = "fps";

    /// <summary>Cuadros por segundo del juego en primer plano (PresentMon).</summary>
    public const string Fps = "fps";

    /// <summary>Tiempo medio entre cuadros, en ms.</summary>
    public const string FrameTime = "fps.frametime";

    /// <summary>«1 % low»: FPS del 1 % de cuadros más lentos de los últimos 10 s.</summary>
    public const string FpsLow1 = "fps.low1";

    /// <summary>API gráfica del juego (DX9, DX11, DX12, Vulkan, OpenGL).</summary>
    public const string GraphicsApi = "fps.api";

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
        Fps, GraphicsApi, FrameTime, FpsLow1,
        CpuLoad, CpuTemperature, CpuClock, CpuPower, CpuFan,
        GpuLoad, GpuTemperature, GpuHotspot, GpuVram, GpuPower, GpuFan,
        RamUsed, RamPercent,
        NetThroughput
    };

    /// <summary>Selección inicial: lo que muestra la barra al activarla.</summary>
    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        Fps,
        CpuLoad, CpuTemperature, CpuClock,
        GpuLoad, GpuTemperature, GpuVram,
        RamUsed
    };

    /// <summary>Grupos con color propio.</summary>
    public static readonly IReadOnlyList<string> Groups = new[]
    {
        FpsGroup, CpuGroup, GpuGroup, RamGroup, NetGroup
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Known.Contains(key.Trim());

    /// <summary>Grupo al que pertenece una clave; vacío si no se reconoce.</summary>
    public static string GroupOf(string key)
    {
        if (string.Equals(key, Fps, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("fps.", StringComparison.OrdinalIgnoreCase))
            return FpsGroup;

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
    public static bool IsFps(string key) => GroupOf(key) == FpsGroup;

    /// <summary>
    /// Colores iniciales por grupo, al estilo de la barra de NVIDIA: CPU
    /// amarillo, GPU verde, RAM violeta y red cian.
    /// </summary>
    public static Dictionary<string, string> DefaultGroupColors() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [FpsGroup] = "#FFFF9F43",
            [CpuGroup] = "#FFFFD166",
            [GpuGroup] = "#FF7BD88F",
            [RamGroup] = "#FFC792EA",
            [NetGroup] = "#FF56B6C2"
        };
}
