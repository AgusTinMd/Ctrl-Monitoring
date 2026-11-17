using System.Text.Json;
using System.Text.Json.Serialization;

namespace FanCtrl.Core.Configuration;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigStore(string? configPath = null)
    {
        ConfigPath = configPath ?? AppPaths.ConfigFile;
    }

    public string ConfigPath { get; }

    public string? LastError { get; private set; }

    public AppConfig Load()
    {
        LastError = null;

        try
        {
            if (!File.Exists(ConfigPath))
            {
                AppConfig fresh = new();
                fresh.Normalize();
                return fresh;
            }

            string json = File.ReadAllText(ConfigPath);
            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

            if (config is null)
                return CreateDefaultWithBackup();

            // Los archivos v1 traían curvas y perfiles; al migrarlos se
            // descartan esos campos y el próximo guardado queda en v2.
            config.SchemaVersion = 2;
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return CreateDefaultWithBackup();
        }
    }

    public bool Save(AppConfig config)
    {
        LastError = null;

        try
        {
            config.Normalize();

            string? directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = ConfigPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions));
            File.Move(temporaryPath, ConfigPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private AppConfig CreateDefaultWithBackup()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string backup = ConfigPath + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(ConfigPath, backup, overwrite: false);
            }
        }
        catch
        {
            // El backup es deseable, no obligatorio.
        }

        AppConfig fresh = new();
        fresh.Normalize();
        return fresh;
    }
}

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FanCtrl");

    public static string ConfigFile => Path.Combine(AppDataDirectory, "config.json");

    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}
