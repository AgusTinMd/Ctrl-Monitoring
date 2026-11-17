namespace FanCtrl.Core.Hardware;

/// <summary>
/// Foto inmutable de todos los sensores en un instante. La UI la consume
/// en lugar de leer los sensores de LibreHardwareMonitor desde el hilo de WPF.
/// </summary>
public sealed record HardwareSnapshot(
    DateTime TimestampUtc,
    IReadOnlyList<SensorReading> Sensors)
{
    public static HardwareSnapshot Empty { get; } = new(
        DateTime.MinValue,
        Array.Empty<SensorReading>());
}
