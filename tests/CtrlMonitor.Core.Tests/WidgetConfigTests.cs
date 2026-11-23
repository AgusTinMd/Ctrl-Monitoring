using CtrlMonitor.Core.Configuration;

namespace CtrlMonitor.Core.Tests;

public class WidgetConfigTests
{
    [Fact]
    public void Widget_IsOffByDefault()
    {
        var config = new AppConfig();

        Assert.False(config.WidgetEnabled);
        Assert.True(config.WidgetTopmost);
        Assert.Null(config.WidgetLeft);
        Assert.Null(config.WidgetTop);
    }

    [Fact]
    public void Normalize_DropsInvalidWidgetPosition()
    {
        var config = new AppConfig { WidgetLeft = double.NaN, WidgetTop = double.PositiveInfinity };

        config.Normalize();

        Assert.Null(config.WidgetLeft);
        Assert.Null(config.WidgetTop);
    }
}
