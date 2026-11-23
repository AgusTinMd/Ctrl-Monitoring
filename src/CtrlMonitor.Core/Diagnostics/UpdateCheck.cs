using System.Globalization;
using System.Text.Json;

namespace CtrlMonitor.Core.Diagnostics;

/// <summary>Última versión publicada en GitHub.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Url);

/// <summary>
/// Lógica del aviso de versión nueva: interpreta la respuesta de la API de
/// GitHub y compara versiones. Sin red, para poder probarla.
/// </summary>
public static class UpdateCheck
{
    /// <summary>Última versión publicada (no borradores ni previas).</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/AgusTinMd/Ctrl-Monitoring/releases/latest";

    /// <summary>La consulta a GitHub sale como mucho una vez por semana.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    /// <summary>True si nunca se consultó o pasó una semana desde la última vez.</summary>
    public static bool IsDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not { } last || last > nowUtc || nowUtc - last >= Interval;

    /// <summary>Solo se abren páginas del propio repositorio.</summary>
    public static bool IsRepositoryUrl(string? url) =>
        url is not null && url.StartsWith("https://github.com/AgusTinMd/Ctrl-Monitoring/", StringComparison.OrdinalIgnoreCase);

    /// <summary>«v2.13.0» o «2.13.0» → 2.13.0. Null si no es una versión.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        string value = tag.Trim().TrimStart('v', 'V');

        return Version.TryParse(value, out Version? version) ? Normalize(version) : null;
    }

    /// <summary>Lee <c>tag_name</c> y <c>html_url</c> de /releases/latest.</summary>
    public static ReleaseInfo? ParseLatestRelease(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                return null;

            if (root.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True)
                return null;

            string? tag = root.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() : null;
            string? url = root.TryGetProperty("html_url", out JsonElement u) ? u.GetString() : null;
            Version? version = ParseTag(tag);

            // Solo se abre una página del repositorio, nunca otra dirección.
            if (version is null || !IsRepositoryUrl(url))
            {
                return null;
            }

            return new ReleaseInfo(version, tag!, url!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True si <paramref name="latest"/> es más nueva que la versión instalada.</summary>
    public static bool IsNewer(Version latest, Version current) =>
        Normalize(latest) > Normalize(current);

    /// <summary>«2.13.0» sin el cuarto número, para comparar igual que los tags.</summary>
    public static string Display(Version version) =>
        string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}");

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));
}
