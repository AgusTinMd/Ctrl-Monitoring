namespace CtrlMonitor.Core.Hardware;

/// <summary>Tipo de magnitud que publica un sensor de LibreHardwareMonitor.</summary>
public enum SensorKind
{
    Temperature = 0,
    Fan = 1,
    Power = 2,
    Voltage = 3,
    Load = 4,
    Pwm = 5,
    Memory = 6
}

/// <summary>
/// Lectura inmutable de un sensor. La UI consume esto en lugar de tocar
/// LibreHardwareMonitor directamente.
/// </summary>
public sealed record SensorReading(
    string Id,
    string HardwareId,
    string HardwareName,
    string HardwareType,
    string Name,
    SensorKind Kind,
    int Index,
    double Value,
    string Unit)
{
    /// <summary>Valor formateado según la magnitud, sin unidad.</summary>
    public string ValueText => Kind switch
    {
        SensorKind.Temperature => Value.ToString("0.0"),
        SensorKind.Fan => Value.ToString("0"),
        SensorKind.Power => Value.ToString("0.0"),
        SensorKind.Voltage => Value.ToString("0.00"),
        SensorKind.Load or SensorKind.Pwm => Value.ToString("0"),
        SensorKind.Memory => Value.ToString("0"),
        _ => Value.ToString("0.0")
    };
}
