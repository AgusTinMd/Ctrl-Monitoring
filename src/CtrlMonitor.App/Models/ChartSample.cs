using CtrlMonitor.Core.Inventory;

namespace CtrlMonitor.App.Models;

/// <summary>Una muestra de temperaturas, uso, memoria y red para el modo gráfico.</summary>
public sealed record ChartSample(
    DateTime TimestampUtc,
    double? Cpu,
    double? Gpu,
    double? Board,
    double? Hotspot,
    double? RamPercent,
    double? VramPercent,
    double? NetDownMbs,
    double? NetUpMbs,
    double? CpuLoad,
    double? GpuLoad,
    IReadOnlyDictionary<string, DiskActivity>? Disks,
    IReadOnlyList<double>? Threads = null);
