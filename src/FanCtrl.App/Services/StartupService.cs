using System.Diagnostics;
using FanCtrl.Core.Diagnostics;

namespace FanCtrl.App.Services;

/// <summary>
/// Inicio con Windows mediante una tarea programada. Al correr con /RL HIGHEST
/// evitamos el cartel de UAC en cada inicio (la app necesita administrador).
/// </summary>
public static class StartupService
{
    private const string TaskName = "FanCtrl";

    public static bool IsEnabled()
    {
        ProcessResult result = Run("/Query", "/TN", TaskName);
        return result.ExitCode == 0;
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
