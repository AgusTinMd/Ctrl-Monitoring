using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using FanCtrl.Core.Diagnostics;

namespace FanCtrl.App.Services;

internal enum ElevationResult
{
    Started,
    Cancelled,
    Unavailable,
    Failed
}

/// <summary>
/// Garantiza que la app corra con permisos de administrador. El manifiesto ya
/// lo exige para FanCtrl.exe, pero iniciar con `dotnet FanCtrl.dll` lo evita;
/// en ese caso se relanza el apphost con el verbo runas.
/// </summary>
internal static class ElevationService
{
    public const string RetryArgument = "--elevation-retry";

    private const int ErrorCancelled = 1223;

    public static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static ElevationResult RelaunchElevated(string[] args)
    {
        string? executable = AppLaunch.ExecutablePath;

        if (executable is null)
        {
            AppLog.Warn("No se encontró FanCtrl.exe para solicitar la elevación.");
            return ElevationResult.Unavailable;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = BuildArguments(args)
        };

        try
        {
            Process.Start(startInfo);
            AppLog.Info("Se solicitó reiniciar con permisos de administrador.");
            return ElevationResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            AppLog.Info("El usuario canceló la solicitud de elevación.");
            return ElevationResult.Cancelled;
        }
        catch (Exception ex)
        {
            AppLog.Error("No se pudo reiniciar como administrador", ex);
            return ElevationResult.Failed;
        }
    }

    private static string BuildArguments(IEnumerable<string> args) =>
        string.Join(' ', args
            .Where(arg => !arg.Equals(RetryArgument, StringComparison.OrdinalIgnoreCase))
            .Append(RetryArgument)
            .Select(Quote));

    private static string Quote(string value) =>
        value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
}
