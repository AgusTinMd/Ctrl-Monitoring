using System.IO;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Resuelve el ejecutable real de la app. Si se inició con `dotnet CtrlMonitor.dll`,
/// <see cref="Environment.ProcessPath"/> apunta a dotnet.exe; en ese caso se usa
/// el apphost CtrlMonitor.exe que está junto a la DLL.
/// </summary>
internal static class AppLaunch
{
    private const string HostName = "CtrlMonitor.exe";

    public static string? ExecutablePath
    {
        get
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, HostName);

            if (File.Exists(candidate))
                return candidate;

            string? processPath = Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(processPath) &&
                !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            return null;
        }
    }
}
