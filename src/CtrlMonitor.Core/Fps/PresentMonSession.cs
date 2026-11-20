using System.Diagnostics;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.Core.Fps;

/// <summary>
/// Mide los FPS de un proceso con PresentMon (Intel, MIT), que lee los eventos
/// de presentación de Windows (ETW). No toca el proceso del juego: no inyecta
/// nada ni lee su memoria, así que no hay riesgo con anticheats.
/// </summary>
public sealed class PresentMonSession : IDisposable
{
    public const string ExecutableName = "PresentMon.exe";
    private const string SessionName = "CtrlMonitor-FPS";

    /// <summary>Si no llegan cuadros en este tiempo, la lectura deja de valer.</summary>
    private const long StaleAfterMs = 2000;

    /// <summary>Tiempo que se sigue midiendo un juego después de salir de él.</summary>
    private const long IdleGraceMs = 30_000;

    private long _idleSince;

    private readonly string _executable;
    private readonly FpsCalculator _calculator = new();
    private readonly object _gate = new();

    private Process? _process;
    private int _targetId;
    private long _lastFrameTick;
    private bool _disposed;

    public PresentMonSession(string? executable = null)
    {
        _executable = executable ?? Path.Combine(AppContext.BaseDirectory, ExecutableName);
    }

    /// <summary>False si PresentMon no está junto a la app: los FPS se muestran como "--".</summary>
    public bool IsAvailable => File.Exists(_executable);

    /// <summary>Proceso que se está midiendo; 0 si ninguno.</summary>
    public int TargetId
    {
        get
        {
            lock (_gate)
                return _targetId;
        }
    }

    /// <summary>
    /// Mide el proceso indicado. Si cambia, se reinicia PresentMon. Con 0 la
    /// medición sigue un rato (<see cref="IdleGraceMs"/>) y después se detiene.
    /// </summary>
    public void SetTarget(int processId)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            long now = Environment.TickCount64;

            // Salir un momento del juego (Alt+Tab, abrir la app) no corta la
            // medición: así al volver los FPS aparecen al instante y no se
            // reinicia PresentMon a cada rato.
            if (processId == 0)
            {
                if (_targetId != 0 && _idleSince == 0)
                    _idleSince = now;

                if (_targetId != 0 && now - _idleSince > IdleGraceMs)
                {
                    StopLocked();
                    _targetId = 0;
                    _idleSince = 0;
                }

                return;
            }

            _idleSince = 0;

            if (processId == _targetId && _process is { HasExited: false })
                return;

            StopLocked();
            _targetId = processId;

            if (IsAvailable)
                StartLocked(processId);
        }
    }

    /// <summary>Lectura actual, o null si no hay cuadros recientes.</summary>
    public FpsReading? Current()
    {
        lock (_gate)
        {
            if (_targetId == 0 || Environment.TickCount64 - Interlocked.Read(ref _lastFrameTick) > StaleAfterMs)
                return null;
        }

        return _calculator.Current();
    }

    private void StartLocked(int processId)
    {
        _calculator.Reset();
        Interlocked.Exchange(ref _lastFrameTick, 0);

        var info = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_executable) ?? AppContext.BaseDirectory
        };

        foreach (string argument in new[]
                 {
                     "--process_id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--output_stdout",
                     "--no_console_stats",
                     "--track_frame_type",
                     "--no_track_input",
                     "--no_track_gpu",
                     "--terminate_on_proc_exit",
                     "--session_name", SessionName,
                     "--stop_existing_session"
                 })
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => OnLine(process, e.Data);
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    AppLog.Warn("PresentMon: " + e.Data.Trim());
            };

            // Se asigna antes de leer para no descartar las primeras líneas.
            _process = process;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            AppLog.Info($"FPS: midiendo el proceso {processId} con PresentMon.");
        }
        catch (Exception ex)
        {
            AppLog.Warn("No se pudo iniciar PresentMon: " + ex.Message);
            _process = null;
        }
    }

    private void OnLine(Process source, string? line)
    {
        // Las líneas de un PresentMon anterior se descartan.
        if (!ReferenceEquals(source, _process) || line is null)
            return;

        bool hadHeader = _calculator.HasHeader;
        _calculator.AddLine(line);

        if (hadHeader)
            Interlocked.Exchange(ref _lastFrameTick, Environment.TickCount64);
    }

    private void StopLocked()
    {
        Process? process = _process;
        _process = null;
        _calculator.Reset();

        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("No se pudo cerrar PresentMon: " + ex.Message);
        }
        finally
        {
            process.Dispose();
        }

        // Al cerrar PresentMon a la fuerza, su sesión de ETW queda abierta en
        // Windows: se le pide a PresentMon que la cierre.
        TerminateSession();
    }

    private void TerminateSession()
    {
        if (!IsAvailable)
            return;

        try
        {
            var info = new ProcessStartInfo(_executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            info.ArgumentList.Add("--session_name");
            info.ArgumentList.Add(SessionName);
            info.ArgumentList.Add("--terminate_existing_session");

            using Process? cleanup = Process.Start(info);

            if (cleanup is not null && !cleanup.WaitForExit(3000))
                cleanup.Kill();
        }
        catch (Exception ex)
        {
            AppLog.Warn("No se pudo cerrar la sesión de PresentMon: " + ex.Message);
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
        }
    }
}
