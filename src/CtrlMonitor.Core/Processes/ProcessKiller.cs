using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace CtrlMonitor.Core.Processes;

/// <summary>Resultado de intentar finalizar un proceso.</summary>
public enum KillResult
{
    Done,
    NotFound,
    Protected,
    AccessDenied,
    Failed
}

/// <summary>
/// Finaliza procesos a pedido del usuario. Se niega con los procesos críticos
/// de Windows (cerrarlos cuelga o reinicia el equipo) y con la propia app.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProcessKiller
{
    // Procesos sin los que Windows no puede seguir: cerrarlos provoca un
    // pantallazo azul o un reinicio. El Administrador de tareas también los protege.
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso"
    };

    /// <summary>Indica si el proceso no se puede finalizar desde la app.</summary>
    public static bool IsProtected(int id, string name) =>
        id <= 4 || CriticalNames.Contains(name) || id == Environment.ProcessId;

    /// <summary>
    /// Finaliza el proceso si sigue siendo el mismo (mismo PID y momento de
    /// inicio), opcionalmente con todos sus procesos hijos.
    /// </summary>
    public static KillResult Kill(int id, long startTicks, string name, bool entireTree)
    {
        if (IsProtected(id, name))
            return KillResult.Protected;

        Process process;

        try
        {
            process = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return KillResult.NotFound;
        }

        using (process)
        {
            try
            {
                // Windows reutiliza los PID: si el proceso ya no es el mismo, no se toca.
                if (startTicks != 0 && process.StartTime.ToUniversalTime().Ticks != startTicks)
                    return KillResult.NotFound;

                process.Kill(entireTree);
                process.WaitForExit(3000);
                return KillResult.Done;
            }
            catch (Win32Exception)
            {
                return KillResult.AccessDenied;
            }
            catch (InvalidOperationException)
            {
                return KillResult.NotFound;
            }
            catch (Exception)
            {
                return KillResult.Failed;
            }
        }
    }
}
