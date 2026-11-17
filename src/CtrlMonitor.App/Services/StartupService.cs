using System.Diagnostics;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Inicio con Windows mediante una tarea programada. Al correr con /RL HIGHEST
/// evitamos el cartel de UAC en cada inicio (la app necesita administrador).
/// </summary>
public static class StartupService
{
    private const string TaskName = "CtrlMonitor";

    /// <summary>Tarea creada por las versiones que se llamaban FanCtrl.</summary>
    private const string LegacyTaskName = "FanCtrl";

    public static bool IsEnabled()
    {
        ProcessResult result = Run("/Query", "/TN", TaskName);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Si la tarea de inicio existe pero ejecuta otro exe (la app se instaló en
    /// otra carpeta, se movió o quedó una copia vieja), la vuelve a crear
    /// apuntando al ejecutable actual.
    /// </summary>
    /// <returns><c>true</c> si la tarea se corrigió.</returns>
    public static bool EnsureTaskPointsHere()
    {
        string? executable = AppLaunch.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executable))
            return false;

        ProcessResult query = Run("/Query", "/TN", TaskName, "/XML");

        if (query.ExitCode != 0 || ScheduledTaskXml.PointsTo(query.Output, executable))
            return false;

        string previous = ScheduledTaskXml.GetCommand(query.Output) ?? "(desconocido)";

        // Solo se toma la tarea si este exe está en una carpeta que únicamente
        // los administradores pueden modificar. Un build del repo o un zip en
        // Descargas no deben quedar ejecutándose como administrador al iniciar.
        if (!InstallLocation.IsProtectedFromNonAdmins(executable))
        {
            AppLog.Warn(
                $"La tarea de inicio apunta a {previous}; no se cambia a {executable} " +
                "porque esa carpeta la pueden modificar usuarios sin privilegios.");
            return false;
        }

        if (!SetEnabled(true, out string? error))
        {
            AppLog.Warn($"La tarea de inicio apunta a {previous} y no se pudo corregir: {error}");
            return false;
        }

        AppLog.Info($"Tarea de inicio corregida: {previous} -> {executable}");
        return true;
    }

    /// <summary>
    /// Borra la tarea «FanCtrl» de versiones anteriores, que apunta a un
    /// FanCtrl.exe que ya no existe.
    /// </summary>
    /// <returns><c>true</c> si la tarea vieja existía y se borró.</returns>
    public static bool RemoveLegacyTask()
    {
        if (Run("/Query", "/TN", LegacyTaskName).ExitCode != 0)
            return false;

        ProcessResult delete = Run("/Delete", "/TN", LegacyTaskName, "/F");

        if (delete.ExitCode != 0)
        {
            AppLog.Warn($"No se pudo borrar la tarea de inicio anterior: {delete.Output}");
            return false;
        }

        AppLog.Info("Tarea de inicio anterior (FanCtrl) eliminada.");
        return true;
    }

    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;

        if (!enabled)
        {
            ProcessResult delete = Run("/Delete", "/TN", TaskName, "/F");

            // Si la tarea no existía, el resultado también es válido.
            if (delete.ExitCode != 0 && !delete.Output.Contains("no existe", StringComparison.OrdinalIgnoreCase) &&
                !delete.Output.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
            {
                error = FirstLine(delete.Output);
                return false;
            }

            return true;
        }

        string? executable = AppLaunch.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "No se pudo obtener la ruta del ejecutable.";
            return false;
        }

        ProcessResult create = Run(
            "/Create",
            "/TN", TaskName,
            "/TR", $"\"{executable}\" --minimized",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/DELAY", "0000:20",
            "/F");

        if (create.ExitCode != 0)
        {
            error = FirstLine(create.Output);
            AppLog.Warn($"No se pudo crear la tarea de inicio: {create.Output}");
            return false;
        }

        AppLog.Info($"Tarea de inicio creada: {executable}");
        return true;
    }

    private static ProcessResult Run(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);

            using Process process = Process.Start(info)!;

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(8000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // best effort
                }

                return new ProcessResult(-1, "schtasks no respondió.");
            }

            return new ProcessResult(process.ExitCode, output + error);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, ex.Message);
        }
    }

    private static string FirstLine(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
            return "Error desconocido al configurar el inicio automático.";

        return lines.FirstOrDefault(line => line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
               ?? lines[^1];
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);
}
