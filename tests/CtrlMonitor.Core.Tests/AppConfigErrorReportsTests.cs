using CtrlMonitor.Core.Configuration;

namespace CtrlMonitor.Core.Tests;

public class AppConfigErrorReportsTests
{
    [Fact]
    public void ErrorReports_AreOffByDefault()
    {
        Assert.False(new AppConfig().SendErrorReports);
    }

    [Fact]
    public void Normalize_CreatesRandomInstallId()
    {
        var first = new AppConfig();
        var second = new AppConfig();

        first.Normalize();
        second.Normalize();

        Assert.True(Guid.TryParseExact(first.InstallId, "N", out _));
        Assert.NotEqual(first.InstallId, second.InstallId);
    }

    [Fact]
    public void Normalize_KeepsAValidInstallId()
    {
        string id = Guid.NewGuid().ToString("N");
        var config = new AppConfig { InstallId = id };

        config.Normalize();

        Assert.Equal(id, config.InstallId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-id")]
    public void Normalize_ReplacesAnInvalidInstallId(string id)
    {
        var config = new AppConfig { InstallId = id };

        config.Normalize();

        Assert.True(Guid.TryParseExact(config.InstallId, "N", out _));
    }
}
