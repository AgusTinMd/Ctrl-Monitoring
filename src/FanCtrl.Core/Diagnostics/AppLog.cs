using FanCtrl.Core.Configuration;

namespace FanCtrl.Core.Diagnostics;

/// <summary>Log simple a archivo, con rotación diaria. Nunca lanza excepciones.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                string path = Path.Combine(AppPaths.LogDirectory, $"fanctrl-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Un log que rompe la app no sirve.
        }
    }
}
