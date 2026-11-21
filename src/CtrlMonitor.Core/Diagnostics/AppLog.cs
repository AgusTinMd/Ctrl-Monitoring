using CtrlMonitor.Core.Configuration;

namespace CtrlMonitor.Core.Diagnostics;

/// <summary>Log simple a archivo, con rotación diaria. Nunca lanza excepciones.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    /// <summary>
    /// Se dispara con cada línea (nivel, mensaje, excepción). Lo usa el envío
    /// opcional de informes de errores; si un suscriptor falla, se ignora.
    /// </summary>
    public static event Action<string, string, Exception?>? Written;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}", exception);

    private static void Write(string level, string message, Exception? exception = null)
    {
        WriteFile(level, message);

        try
        {
            Written?.Invoke(level, message, exception);
        }
        catch
        {
            // El envío de informes nunca puede romper el log.
        }
    }

    private static void WriteFile(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                string path = Path.Combine(AppPaths.LogDirectory, $"ctrlmonitor-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Un log que rompe la app no sirve.
        }
    }
}
