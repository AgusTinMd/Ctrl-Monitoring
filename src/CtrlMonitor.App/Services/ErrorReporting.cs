using System.IO;
using System.Text.RegularExpressions;
using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Diagnostics;
using Sentry;
using Sentry.Protocol;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Envío opcional de informes de errores a Sentry. Solo funciona si el usuario
/// lo activa en Ajustes. Se envían los errores (no el log completo) con la
/// versión, Windows y el hardware; el nombre de usuario y el de la PC se
/// borran antes de salir del equipo.
/// </summary>
internal static class ErrorReporting
{
    /// <summary>
    /// DSN del proyecto en sentry.io. Es público a propósito: solo permite
    /// enviar errores, no leerlos. Vacío = no se envía nada.
    /// </summary>
    private const string Dsn =
        "https://a2f08a2fc64e9ae8c90f7d7ed8845a7a@o4512470528360448.ingest.de.sentry.io/4512470540157008";

    /// <summary>Tope de informes por sesión, por si un error se repite en bucle.</summary>
    private const int MaxEventsPerSession = 25;

    private static readonly object Gate = new();
    private static readonly HashSet<string> Sent = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> HardwareTags = new();

    private static IDisposable? _sdk;
    private static string _installId = string.Empty;
    private static int _eventCount;
    private static bool _subscribed;

    /// <summary>True si esta compilación tiene un DSN y puede enviar informes.</summary>
    public static bool IsConfigured => Dsn.Length > 0;

    public static bool IsEnabled
    {
        get
        {
            lock (Gate)
                return _sdk is not null;
        }
    }

    /// <summary>Activa o desactiva el envío según la casilla de Ajustes.</summary>
    public static void Configure(bool enabled, string installId)
    {
        lock (Gate)
        {
            _installId = installId;

            if (!_subscribed)
            {
                AppLog.Written += OnLogWritten;
                _subscribed = true;
            }

            if (enabled && IsConfigured && _sdk is null)
                Start();
            else if (!enabled && _sdk is not null)
                Stop();
        }
    }

    /// <summary>Modelo de CPU, GPU y placa: ayudan a ver si un error es de un hardware puntual.</summary>
    public static void SetHardware(string cpu, string gpu, string board)
    {
        lock (Gate)
        {
            HardwareTags["cpu"] = cpu;
            HardwareTags["gpu"] = gpu;
            HardwareTags["board"] = board;

            if (_sdk is not null)
                ApplyScope();
        }
    }

    /// <summary>
    /// Envía un informe de prueba (opción <c>--test-error-report</c>) para
    /// comprobar que llegan. Solo si el usuario activó los informes.
    /// </summary>
    public static void SendTest()
    {
        if (!IsEnabled)
        {
            AppLog.Warn("Prueba de informes: están desactivados en Ajustes; no se envía nada.");
            return;
        }

        SentrySdk.CaptureMessage("Prueba de informe de errores", SentryLevel.Info);
        SentrySdk.Flush(TimeSpan.FromSeconds(5));
        AppLog.Info("Prueba de informes enviada.");
    }

    /// <summary>Espera a que salgan los informes pendientes (al cerrarse por un error).</summary>
    public static void Flush()
    {
        if (IsEnabled)
            SentrySdk.Flush(TimeSpan.FromSeconds(3));
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (_sdk is not null)
                Stop();
        }
    }

    private static void Start()
    {
        string version = AppVersion();

        _sdk = SentrySdk.Init(options =>
        {
            options.Dsn = Dsn;
            options.Release = "ctrl-monitor@" + version;
            options.Environment = version == "dev" ? "development" : "production";
            options.IsGlobalModeEnabled = true;
            options.SendDefaultPii = false;
            options.AutoSessionTracking = false;
            options.AttachStacktrace = true;
            options.MaxBreadcrumbs = 60;

            // Sin conexión, los informes esperan en disco y salen al volver a abrir la app.
            options.CacheDirectoryPath = Path.Combine(AppPaths.AppDataDirectory, "error-reports");

            // Los errores fatales ya pasan por AppLog.Error: así no se envían dos veces.
            options.DisableAppDomainUnhandledExceptionCapture();

            options.SetBeforeSend((sentryEvent, _) => Scrub(sentryEvent));
        });

        ApplyScope();
        AppLog.Info("Informes de errores activados.");
    }

    private static void Stop()
    {
        _sdk?.Dispose();
        _sdk = null;
        AppLog.Info("Informes de errores desactivados.");
    }

    private static void ApplyScope()
    {
        string installId = _installId;
        var tags = new Dictionary<string, string>(HardwareTags);

        SentrySdk.ConfigureScope(scope =>
        {
            scope.User = new SentryUser { Id = installId };

            foreach ((string key, string value) in tags)
                scope.SetTag(key, Clean(value));
        });
    }

    private static void OnLogWritten(string level, string message, Exception? exception)
    {
        if (!IsEnabled)
            return;

        string clean = Clean(message);

        if (level != "ERROR")
        {
            // Lo que pasó antes del error viaja con él como contexto.
            SentrySdk.AddBreadcrumb(
                clean,
                category: "log",
                level: level == "WARN" ? BreadcrumbLevel.Warning : BreadcrumbLevel.Info);
            return;
        }

        // El mismo error se envía una sola vez por sesión, y con un tope total.
        string key = exception is null
            ? clean
            : $"{exception.GetType().FullName}: {Clean(exception.Message)}";

        lock (Gate)
        {
            if (_eventCount >= MaxEventsPerSession || !Sent.Add(key))
                return;

            _eventCount++;
        }

        if (exception is not null)
        {
            SentrySdk.CaptureException(exception, scope => scope.SetExtra("log", clean));
        }
        else
        {
            SentrySdk.CaptureMessage(clean, SentryLevel.Error);
        }
    }

    /// <summary>Último filtro antes de enviar: sin nombre de la PC ni del usuario.</summary>
    private static SentryEvent? Scrub(SentryEvent sentryEvent)
    {
        sentryEvent.ServerName = null;
        sentryEvent.Contexts.Device.Name = null;

        // Sin IP, Sentry usa la de la conexión para calcular país y ciudad. Una
        // IP nula a propósito evita que guarde la ubicación.
        sentryEvent.User.IpAddress = "0.0.0.0";

        if (sentryEvent.Message is { } message)
        {
            if (message.Message is not null)
                message.Message = Clean(message.Message);

            if (message.Formatted is not null)
                message.Formatted = Clean(message.Formatted);
        }

        foreach (SentryException exception in sentryEvent.SentryExceptions ?? Enumerable.Empty<SentryException>())
        {
            if (exception.Value is not null)
                exception.Value = Clean(exception.Value);
        }

        return sentryEvent;
    }

    /// <summary>Reemplaza el nombre de usuario y el de la PC por marcadores.</summary>
    internal static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string result = text;
        string user = Environment.UserName;
        string machine = Environment.MachineName;

        if (user.Length > 1)
            result = Regex.Replace(result, Regex.Escape(user), "<usuario>", RegexOptions.IgnoreCase);

        if (machine.Length > 1)
            result = Regex.Replace(result, Regex.Escape(machine), "<equipo>", RegexOptions.IgnoreCase);

        // Rutas de perfil con otro nombre (por ejemplo, la forma corta AGUSTI~1).
        result = Regex.Replace(result, @"(?i)(\\Users\\)[^\\/:*?""<>|\r\n]+", "$1<usuario>");

        return result;
    }

    private static string AppVersion()
    {
        Version? version = typeof(ErrorReporting).Assembly.GetName().Version;

        return version is null || (version.Major == 0 && version.Minor == 0 && version.Build <= 0)
            ? "dev"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
