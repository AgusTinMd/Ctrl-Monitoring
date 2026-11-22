using System.Diagnostics;
using Microsoft.Win32;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Abre los formularios de GitHub para reportar un error o sugerir una mejora,
/// con la versión, Windows y el hardware ya completados. El usuario ve todo
/// antes de enviarlo.
/// </summary>
internal static class FeedbackLinks
{
    private const string NewIssueUrl = "https://github.com/AgusTinMd/Ctrl-Monitoring/issues/new";

    public static void ReportBug(string version, string hardware) =>
        Open(NewIssueUrl
             + "?template=bug_report.yml"
             + "&version=" + Uri.EscapeDataString(version)
             + "&windows=" + Uri.EscapeDataString(WindowsVersion())
             + "&hardware=" + Uri.EscapeDataString(hardware));

    public static void Suggest(string version) =>
        Open(NewIssueUrl
             + "?template=feature_request.yml"
             + "&version=" + Uri.EscapeDataString(version));

    /// <summary>«Windows 11 24H2 (26100)», leído del registro.</summary>
    internal static string WindowsVersion()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            int build = Environment.OSVersion.Version.Build;
            string name = build >= 22000 ? "Windows 11" : "Windows 10";
            string? release = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string;

            return string.IsNullOrWhiteSpace(release) ? $"{name} ({build})" : $"{name} {release} ({build})";
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    /// <summary>
    /// La app corre como administrador: abrir la URL a través del Explorador
    /// hace que el navegador se abra con los permisos normales del usuario.
    /// </summary>
    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("No se pudo abrir el formulario de GitHub: " + ex.Message);
        }
    }
}
