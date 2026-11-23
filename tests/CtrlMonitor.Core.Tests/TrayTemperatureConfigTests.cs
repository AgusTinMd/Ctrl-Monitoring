using CtrlMonitor.Core.Configuration;

namespace CtrlMonitor.Core.Tests;

public class TrayTemperatureConfigTests
{
    [Fact]
    public void TrayTemperature_IsOffByDefaultAndUsesCpu()
    {
        var config = new AppConfig();

        Assert.False(config.TrayShowTemperature);
        Assert.Equal("cpu", config.TrayTemperatureSource);
    }

    [Theory]
    [InlineData("GPU", "gpu")]
    [InlineData("cpu", "cpu")]
    [InlineData("otra", "cpu")]
    [InlineData(null, "cpu")]
    public void Normalize_FixesTheSource(string? source, string expected)
    {
        var config = new AppConfig { TrayTemperatureSource = source! };

        config.Normalize();

        Assert.Equal(expected, config.TrayTemperatureSource);
    }
}
