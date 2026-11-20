using CtrlMonitor.Core.Configuration;

namespace CtrlMonitor.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public ConfigStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "CtrlMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new ConfigStore(_path);
        AppConfig config = new()
        {
            RefreshIntervalMs = 2000,
            StartMinimized = true,
            MinimizeToTray = false,
            RunAtStartup = true,
            ShowNavigation = false
        };

        Assert.True(store.Save(config));

        AppConfig loaded = store.Load();
        Assert.Equal(2000, loaded.RefreshIntervalMs);
        Assert.True(loaded.StartMinimized);
        Assert.False(loaded.MinimizeToTray);
        Assert.True(loaded.RunAtStartup);
        Assert.False(loaded.ShowNavigation);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOverlaySettings()
    {
        var store = new ConfigStore(_path);
        AppConfig config = new();
        config.Overlay.Enabled = true;
        config.Overlay.TextColor = "#FF6CB6FF";
        config.Overlay.TextSize = 18;
        config.Overlay.BackgroundOpacity = 25;
        config.Overlay.Metrics = new List<string> { "cpu.load", "gpu.temp", "net.throughput" };
        config.Overlay.GroupColors["cpu"] = "#FFFF0000";
        config.Overlay.GroupColors["net"] = string.Empty;
        config.Overlay.Left = 120.5;
        config.Overlay.Top = 42;

        Assert.True(store.Save(config));

        AppConfig loaded = store.Load();
        Assert.True(loaded.Overlay.Enabled);
        Assert.Equal("#FF6CB6FF", loaded.Overlay.TextColor);
        Assert.Equal(18, loaded.Overlay.TextSize);
        Assert.Equal(25, loaded.Overlay.BackgroundOpacity);
        Assert.Equal(new[] { "cpu.load", "gpu.temp", "net.throughput" }, loaded.Overlay.Metrics);
        Assert.Equal("#FFFF0000", loaded.Overlay.GroupColors["cpu"]);
        Assert.Equal(string.Empty, loaded.Overlay.GroupColors["net"]);
        Assert.Equal(120.5, loaded.Overlay.Left);
        Assert.Equal(42, loaded.Overlay.Top);
    }

    [Fact]
    public void Load_ConfigWithoutOverlaySection_UsesDefaults()
    {
        File.WriteAllText(_path, """
        {
          "SchemaVersion": 2,
          "RefreshIntervalMs": 1000,
          "MinimizeToTray": true
        }
        """);

        var store = new ConfigStore(_path);
        AppConfig config = store.Load();

        Assert.NotNull(config.Overlay);
        Assert.False(config.Overlay.Enabled);
        Assert.Equal(14, config.Overlay.TextSize);
        Assert.Equal(60, config.Overlay.BackgroundOpacity);
        Assert.Equal(CtrlMonitor.Core.Overlay.OverlayColors.DefaultText, config.Overlay.TextColor);
        Assert.Equal(
            CtrlMonitor.Core.Overlay.OverlayMetricKeys.Defaults.ToArray(),
            config.Overlay.Metrics.ToArray());
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new ConfigStore(Path.Combine(_directory, "nope.json"));
        AppConfig config = store.Load();

        Assert.Equal(1000, config.RefreshIntervalMs);
        Assert.True(config.MinimizeToTray);
        Assert.False(config.StartMinimized);
        Assert.True(config.ShowNavigation);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndBacksUp()
    {
        File.WriteAllText(_path, "{ esto no es json");
        var store = new ConfigStore(_path);

        AppConfig config = store.Load();

        Assert.Equal(1000, config.RefreshIntervalMs);
        Assert.NotEmpty(Directory.GetFiles(_directory, "*.bak-*"));
    }

    [Fact]
    public void Normalize_ClampsRefreshInterval()
    {
        AppConfig tooFast = new() { RefreshIntervalMs = 10 };
        tooFast.Normalize();
        Assert.Equal(500, tooFast.RefreshIntervalMs);

        AppConfig tooSlow = new() { RefreshIntervalMs = 60_000 };
        tooSlow.Normalize();
        Assert.Equal(10_000, tooSlow.RefreshIntervalMs);
    }

    [Fact]
    public void Load_LegacyControlConfig_IgnoresObsoleteFields()
    {
        File.WriteAllText(_path, """
        {
          "SchemaVersion": 1,
          "ControlEnabled": true,
          "ActiveProfile": "Rendimiento",
          "MinPwm": 35,
          "EmergencyTemperatureC": 90
        }
        """);

        var store = new ConfigStore(_path);
        AppConfig config = store.Load();

        Assert.Equal(2, config.SchemaVersion);
        Assert.Equal(1000, config.RefreshIntervalMs);
        Assert.True(config.MinimizeToTray);
        Assert.False(config.StartMinimized);
        Assert.True(config.ShowNavigation);
    }

    [Fact]
    public void Normalize_KeepsSevenChartMetricsAndRemovesDuplicates()
    {
        AppConfig config = new()
        {
            ChartMetrics = new List<string>
            {
                "Cpu", "Gpu", "Ram", "Vram", " Network ", "Hotspot", "Board", "Cpu", "  "
            }
        };

        config.Normalize();

        Assert.Equal(
            new[] { "Cpu", "Gpu", "Ram", "Vram", "Network", "Hotspot", "Board" },
            config.ChartMetrics);
    }

    [Fact]
    public void MigrateFrom_CopiesLegacyConfigWhenMissing()
    {
        string legacy = Path.Combine(_directory, "FanCtrl", "config.json");
        string current = Path.Combine(_directory, "CtrlMonitor", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        new ConfigStore(legacy).Save(new AppConfig { RefreshIntervalMs = 3000 });

        var store = new ConfigStore(current);

        Assert.True(store.MigrateFrom(legacy));
        Assert.Equal(3000, store.Load().RefreshIntervalMs);
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void MigrateFrom_KeepsExistingConfig()
    {
        string legacy = Path.Combine(_directory, "legacy.json");
        new ConfigStore(legacy).Save(new AppConfig { RefreshIntervalMs = 3000 });

        var store = new ConfigStore(_path);
        store.Save(new AppConfig { RefreshIntervalMs = 2000 });

        Assert.False(store.MigrateFrom(legacy));
        Assert.Equal(2000, store.Load().RefreshIntervalMs);
    }

    [Fact]
    public void MigrateFrom_NoLegacyConfig_DoesNothing()
    {
        var store = new ConfigStore(_path);

        Assert.False(store.MigrateFrom(Path.Combine(_directory, "nope.json")));
        Assert.False(File.Exists(_path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
