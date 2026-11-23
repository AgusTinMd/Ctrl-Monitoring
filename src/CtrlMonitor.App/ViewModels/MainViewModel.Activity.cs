using System.Windows.Threading;
using CtrlMonitor.App.Services;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.ViewModels;

/// <summary>
/// Reposo: si nadie mira la app (ventana oculta o minimizada y overlay
/// apagado), no se lee ningún sensor, no corre el administrador de procesos y
/// se devuelve la memoria que la app no usa. Al abrir la ventana o aparecer
/// el overlay, la medición vuelve al instante.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Espera antes de liberar memoria, por si la ventana vuelve enseguida.</summary>
    private static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(3);

    /// <summary>Pasar el mouse por la bandeja relee los sensores como mucho con esta frecuencia.</summary>
    private static readonly TimeSpan PeekInterval = TimeSpan.FromSeconds(3);

    private bool _windowShown;
    private bool _overlayShown;
    private bool _monitoring;
    private bool _activityStarted;
    private DateTime _lastPeekUtc = DateTime.MinValue;
    private DispatcherTimer? _trimTimer;

    /// <summary>True mientras la app mide; false en reposo.</summary>
    public bool IsMonitoring => _monitoring;

    /// <summary>La ventana principal está abierta en pantalla (no oculta ni minimizada).</summary>
    public void SetWindowShown(bool shown)
    {
        _windowShown = shown;
        UpdateActivity();
    }

    /// <summary>La barra del overlay está a la vista.</summary>
    public void SetOverlayShown(bool shown)
    {
        _overlayShown = shown;
        UpdateActivity();
    }

    /// <summary>
    /// En reposo, una lectura suelta para que el tooltip de la bandeja muestre
    /// temperaturas actuales al pasar el mouse.
    /// </summary>
    public void PeekSensors()
    {
        if (_monitoring || _disposed || DateTime.UtcNow - _lastPeekUtc < PeekInterval)
            return;

        _lastPeekUtc = DateTime.UtcNow;
        _hardware.RequestUpdate();
    }

    private void UpdateActivity()
    {
        if (_disposed)
            return;

        bool active = _windowShown || _overlayShown || _widgetShown;

        if (active == _monitoring && _activityStarted)
            return;

        _activityStarted = true;
        _monitoring = active;

        if (active)
        {
            _trimTimer?.Stop();
            _hardware.StartMonitoring(TimeSpan.FromMilliseconds(_config.RefreshIntervalMs));
            Processes.IsActive = SelectedTabIndex == ProcessesTabIndex;
            AppLog.Info(_overlayShown && !_windowShown
                ? "Monitor activo por el overlay."
                : "Monitor activo.");
        }
        else
        {
            _hardware.StopMonitoring();
            Processes.IsActive = false;
            AppLog.Info("Monitor en reposo: sin ventana ni overlay a la vista.");
            ScheduleTrim();
        }

        OnPropertyChanged(nameof(IsMonitoring));
    }

    private void ScheduleTrim()
    {
        if (_trimTimer is null)
        {
            _trimTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _dispatcher) { Interval = TrimDelay };
            _trimTimer.Tick += (_, _) =>
            {
                _trimTimer.Stop();

                if (!_monitoring && !_disposed)
                    MemoryTrimmer.Trim();
            };
        }

        _trimTimer.Stop();
        _trimTimer.Start();
    }
}
