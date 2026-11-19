using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace CtrlMonitor.Core.Processes;

/// <summary>
/// Mide el tráfico de red de cada proceso con ETW, el mismo mecanismo que usa
/// el Administrador de tareas: escucha los eventos de envío y recepción TCP/UDP
/// del proveedor Microsoft-Windows-Kernel-Network. Necesita permisos de
/// administrador; sin ellos no mide nada y la columna queda vacía.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessNetworkMonitor : IDisposable
{
    private const string SessionName = "CtrlMonitor-Network";
    private const string ProviderName = "Microsoft-Windows-Kernel-Network";

    // Palabras clave del proveedor: tráfico IPv4 (0x10) e IPv6 (0x20).
    private const ulong Keywords = 0x10 | 0x20;

    // Envío y recepción TCP/UDP por IPv4 e IPv6. En todos, los dos primeros
    // campos son el PID y la cantidad de bytes.
    private static readonly HashSet<int> TrafficEvents = new() { 10, 11, 26, 27, 42, 43, 58, 59 };

    private readonly ConcurrentDictionary<int, long> _bytes = new();
    private TraceEventSession? _session;
    private Thread? _thread;

    /// <summary>La sesión ETW quedó activa y está midiendo.</summary>
    public bool IsRunning { get; private set; }

    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;

        try
        {
            // Create cierra una sesión con el mismo nombre que haya quedado
            // abierta si la app se cerró de golpe.
            _session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create)
            {
                StopOnDispose = true
            };

            _session.EnableProvider(ProviderName, TraceEventLevel.Informational, Keywords);
            _session.Source.Dynamic.All += OnEvent;

            _thread = new Thread(() =>
            {
                try
                {
                    _session.Source.Process();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    IsRunning = false;
                }
            })
            {
                IsBackground = true,
                Name = "CtrlMonitor ETW red"
            };

            _thread.Start();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            // Sin permisos de administrador o con ETW deshabilitado: sin datos de red.
            LastError = ex.Message;
            Stop();
        }
    }

    private void OnEvent(TraceEvent data)
    {
        // Este método lo llama Windows desde código nativo: una excepción acá
        // cerraría la app sin pasar por los manejadores de errores.
        try
        {
            if (!TrafficEvents.Contains((int)data.ID) || data.EventDataLength < 8)
                return;

            // PID y tamaño son los dos primeros campos (UInt32) de estos eventos.
            byte[] payload = data.EventData();
            int pid = BitConverter.ToInt32(payload, 0);
            int size = BitConverter.ToInt32(payload, 4);

            if (pid > 0 && size > 0)
                _bytes.AddOrUpdate(pid, size, (_, total) => total + size);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Bytes de red de cada proceso desde la llamada anterior.</summary>
    public IReadOnlyDictionary<int, long> TakeBytes()
    {
        var result = new Dictionary<int, long>();

        foreach (int pid in _bytes.Keys)
        {
            if (_bytes.TryRemove(pid, out long bytes))
                result[pid] = bytes;
        }

        return result;
    }

    public void Stop()
    {
        IsRunning = false;

        try
        {
            _session?.Dispose();
        }
        catch
        {
            // best effort
        }

        _session = null;
        _bytes.Clear();
    }

    public void Dispose() => Stop();
}
