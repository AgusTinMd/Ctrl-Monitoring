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

    /// <summary>Mostrar la temperatura en el ícono de la bandeja. Desactivado por defecto.</summary>
    public bool TrayShowTemperature { get; set; }

    /// <summary>Temperatura del ícono: «cpu» o «gpu».</summary>
    public string TrayTemperatureSource { get; set; } = "cpu";

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

    /// <summary>Orden de las métricas en la vista de lista; las que faltan van al final.</summary>
    public List<string> ChartListOrder { get; set; } = new();

    /// <summary>Ancho de las columnas CPU, Memoria, Disco y Red del administrador de procesos.</summary>
    public List<double> ProcessColumnWidths { get; set; } = new() { 96, 116, 100, 96 };

    /// <summary>
    /// Idioma de la interfaz: «auto» (el de Windows) o «es», «en», «pt-BR».
    /// </summary>
    public string Language { get; set; } = AppLanguage.Auto;

    /// <summary>Sección de hilos del CPU desplegada en Inicio; viene plegada.</summary>
    public bool CpuThreadsExpanded { get; set; }

    /// <summary>Preferencias del overlay de métricas.</summary>
    public OverlayConfig Overlay { get; set; } = new();

    /// <summary>
    /// Enviar informes de errores a los desarrolladores. Desactivado hasta que
    /// el usuario lo active en Ajustes.
    /// </summary>
    public bool SendErrorReports { get; set; }

    /// <summary>
    /// Ya se le preguntó al usuario si quiere enviar informes: la pregunta
    /// aparece una sola vez, la primera vez que se abre la ventana.
    /// </summary>
    public bool ErrorReportsAsked { get; set; }

    /// <summary>
    /// Identificador al azar de esta instalación: agrupa los errores de una
    /// misma PC sin saber de quién es.
    /// </summary>
    public string InstallId { get; set; } = string.Empty;

    public void Normalize()
    {
        RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 500, 10000);
        Language = AppLanguage.NormalizeSetting(Language);

        TrayTemperatureSource = string.Equals(TrayTemperatureSource, "gpu", StringComparison.OrdinalIgnoreCase) ? "gpu" : "cpu";

        if (!Guid.TryParseExact(InstallId, "N", out _))
            InstallId = Guid.NewGuid().ToString("N");

        Overlay ??= new OverlayConfig();
        Overlay.Normalize();

        ChartMetrics = (ChartMetrics ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

        // Anchos razonables aunque el archivo traiga valores raros.
        List<double> widths = ProcessColumnWidths ?? new List<double>();
        ProcessColumnWidths = widths.Count == 4 && widths.All(w => w is >= 50 and <= 600)
            ? widths
            : new List<double> { 96, 116, 100, 96 };

        ChartListOrder = (ChartListOrder ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToList();
    }
}
