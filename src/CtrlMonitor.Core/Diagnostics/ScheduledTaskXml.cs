using System.Net;
using System.Text.RegularExpressions;

namespace CtrlMonitor.Core.Diagnostics;

/// <summary>
/// Lectura mínima del XML que devuelve <c>schtasks /Query /XML</c>, para saber
/// a qué ejecutable apunta la tarea de inicio.
/// </summary>
public static partial class ScheduledTaskXml
{
    [GeneratedRegex(@"<Command>\s*(?<path>.*?)\s*</Command>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CommandPattern();

    /// <summary>Ruta del ejecutable de la primera acción, o <c>null</c> si no hay.</summary>
    public static string? GetCommand(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        Match match = CommandPattern().Match(xml);

        if (!match.Success)
            return null;

        string path = WebUtility.HtmlDecode(match.Groups["path"].Value).Trim().Trim('"').Trim();
        return path.Length == 0 ? null : path;
    }

    /// <summary>
    /// Indica si la tarea ejecuta <paramref name="executable"/>. Compara rutas
    /// completas sin distinguir mayúsculas, como hace Windows.
    /// </summary>
    public static bool PointsTo(string? xml, string executable)
    {
        string? command = GetCommand(xml);

        if (command is null || string.IsNullOrWhiteSpace(executable))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(command)),
                Path.GetFullPath(executable),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Una ruta inválida en la tarea cuenta como «apunta a otro lado».
            return false;
        }
    }
}
