using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.Core.Configuration;

/// <summary>
/// Configuración persistida de la app de monitorización. No contiene
/// curvas, perfiles ni límites de control porque la app no escribe PWM.
/// </summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Intervalo de refresco de los sensores, en milisegundos.</summary>
    public int RefreshIntervalMs { get; set; } = 1000;

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool RunAtStartup { get; set; }

    /// <summary>Muestra u oculta la barra lateral de navegación.</summary>
    public bool ShowNavigation { get; set; } = true;

    /// <summary>Métricas visibles en el modo gráfico, en orden.</summary>
    public List<string> ChartMetrics { get; set; } = new() { "Cpu", "Gpu", "Ram" };

    /// <summary>
    /// Vista del modo gráfico: tarjetas (false) o lista con un gráfico grande,
    /// al estilo del Administrador de tareas (true).
    /// </summary>
    public bool ChartListView { get; set; }

    /// <summary>
    /// Idioma de la interfaz: «auto» (el de Windows) o «es», «en», «pt-BR».
    /// </summary>
    public string Language { get; set; } = AppLanguage.Auto;

    public void Normalize()
    {
        RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 500, 10000);
        Language = AppLanguage.NormalizeSetting(Language);

        ChartMetrics = (ChartMetrics ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
    }
}
