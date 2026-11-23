using System.Runtime.Versioning;
using CtrlMonitor.Core.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace CtrlMonitor.Core.Fps;

/// <summary>
/// Averigua qué librerías gráficas cargó el juego escuchando los eventos de
/// carga de imágenes de Windows (ETW). No abre el proceso del juego ni lee su
/// memoria: los anticheats no lo ven. Al empezar, Windows envía la lista de
/// librerías ya cargadas en todos los procesos; se queda solo con las del juego.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GraphicsApiMonitor : IDisposable
{
    private const string SessionName = "CtrlMonitor-GraphicsApi";

    private readonly object _gate = new();
    private readonly HashSet<string> _apis = new(StringComparer.OrdinalIgnoreCase);

    private TraceEventSession? _session;
    private Thread? _thread;
    private int _targetId;
    private bool _disposed;

    /// <summary>
    /// Sigue al proceso indicado. Al cambiar de juego se reinicia la sesión:
    /// así Windows vuelve a enviar las librerías que ya estaban cargadas.
    /// </summary>
    public void SetTarget(int processId)
    {
        lock (_gate)
        {
            if (_disposed || processId == _targetId)
                return;

            StopLocked();
            _targetId = processId;
            _apis.Clear();

            if (processId > 0)
                StartLocked();
        }
    }

    /// <summary>API gráfica del juego según PresentMon y las librerías cargadas.</summary>
    public string? Detect(string? presentRuntime)
    {
        lock (_gate)
        {
            return _targetId == 0 ? null : GraphicsApi.Detect(presentRuntime, _apis.ToArray());
        }
    }

    private void StartLocked()
    {
        try
        {
            var session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create) { StopOnDispose = true };

            // Solo carga de imágenes: muy pocos eventos. En Windows 8 o más
            // nuevo una sesión con nombre propio puede usar el proveedor del
            // kernel sin pelear con otras herramientas por «NT Kernel Logger».
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.ImageLoad);

            session.Source.Kernel.ImageLoad += OnImage;
            session.Source.Kernel.ImageDCStart += OnImage;

            _session = session;
            _thread = new Thread(() =>
            {
                try
                {
                    session.Source.Process();
                }
                catch (Exception ex)
                {
                    AppLog.Warn("API gráfica: la sesión ETW se detuvo: " + ex.Message);
                }
            })
            {
                IsBackground = true,
                Name = "CtrlMonitor ETW API gráfica"
            };

            _thread.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn("API gráfica: no se pudo iniciar la sesión ETW: " + ex.Message);
            StopLocked();
        }
    }

    private void OnImage(ImageLoadTraceData data)
    {
        // Lo llama ETW desde otro hilo: una excepción acá cerraría la app.
        try
        {
            if (data.ProcessID != Volatile.Read(ref _targetId))
                return;

            string name = Path.GetFileName(data.FileName ?? string.Empty);

            if (!GraphicsApi.Modules.TryGetValue(name, out string? api))
                return;

            lock (_gate)
            {
                if (_apis.Add(api))
                    AppLog.Info($"API gráfica: el proceso {data.ProcessID} cargó {name} ({api}).");
            }
        }
        catch
        {
            // Un evento raro no debe cortar la medición.
        }
    }

    private void StopLocked()
    {
        TraceEventSession? session = _session;
        _session = null;
        _thread = null;

        if (session is null)
            return;

        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("API gráfica: no se pudo cerrar la sesión ETW: " + ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            StopLocked();
            _targetId = 0;
            _apis.Clear();
        }
    }
}
