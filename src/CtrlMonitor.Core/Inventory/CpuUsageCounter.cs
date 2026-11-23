using System.Diagnostics;
using System.Runtime.Versioning;

namespace CtrlMonitor.Core.Inventory;

/// <summary>
/// Uso total del CPU según Windows, el mismo dato del Administrador de
/// tareas. A diferencia de LibreHardwareMonitor no ejecuta nada en cada
/// núcleo, así que se puede leer cada segundo sin frenar a un juego.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CpuUsageCounter : IDisposable
{
    private PerformanceCounter? _counter;
    private bool _failed;

    /// <summary>Uso en %, o null si el contador no está disponible.</summary>
    public double? Read()
    {
        if (_failed)
            return null;

        try
        {
            if (_counter is null)
            {
                _counter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true);
                _counter.NextValue();
                return null;
            }

            return Math.Clamp(_counter.NextValue(), 0, 100);
        }
        catch (Exception)
        {
            _failed = true;
            Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        _counter?.Dispose();
        _counter = null;
    }
}
