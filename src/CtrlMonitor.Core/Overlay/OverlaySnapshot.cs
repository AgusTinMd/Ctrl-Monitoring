namespace CtrlMonitor.Core.Overlay;

/// <summary>
/// Valores que el overlay puede mostrar en un instante. Los que no están
/// disponibles quedan en <c>null</c> y el formateador los omite.
/// </summary>
public sealed class OverlaySnapshot
{
    /// <summary>Cuadros mostrados por segundo del juego en primer plano.</summary>
    public double? Fps { get; init; }

    /// <summary>True si parte de los cuadros los genera el driver o el SDK (frame generation).</summary>
    public bool FrameGeneration { get; init; }

    public double? CpuLoad { get; init; }
    public double? CpuTemperature { get; init; }
    public double? CpuClockGhz { get; init; }
    public double? CpuPower { get; init; }
    public int? CpuFanRpm { get; init; }

    public double? GpuLoad { get; init; }
    public double? GpuTemperature { get; init; }
    public double? GpuHotspot { get; init; }
    public double? GpuPower { get; init; }
    public int? GpuFanRpm { get; init; }
    public double? VramUsedGb { get; init; }
    public double? VramTotalGb { get; init; }

    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }
    public double? RamPercent { get; init; }

    public double? NetDownMbs { get; init; }
    public double? NetUpMbs { get; init; }
}
