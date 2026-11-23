using System.Diagnostics;
using System.Net.Http;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Consulta la última versión publicada en GitHub. Solo se usa si el usuario
/// activa «Avisar cuando haya una versión nueva»; no envía ningún dato propio,
/// solo pide la página pública de la última release.
/// </summary>
internal static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellation = default)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(UpdateCheck.LatestReleaseApi, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn($"Actualizaciones: GitHub respondió {(int)response.StatusCode}.");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellation);
            return UpdateCheck.ParseLatestRelease(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AppLog.Warn("Actualizaciones: no se pudo consultar GitHub: " + ex.Message);
            return null;
        }
    }

    /// <summary>Versión instalada; 0.0.0 en compilaciones locales.</summary>
    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Abre la página de la release con los permisos normales del usuario.</summary>
    public static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("No se pudo abrir la página de la versión: " + ex.Message);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // GitHub exige un User-Agent: uno genérico, sin datos del usuario.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CtrlMonitor-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
