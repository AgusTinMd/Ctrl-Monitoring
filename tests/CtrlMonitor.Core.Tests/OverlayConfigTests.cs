using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Tests;

public class OverlayConfigTests
{
    [Fact]
    public void Defaults_AreConservativeAndDisabled()
    {
        var config = new OverlayConfig();

        Assert.False(config.Enabled);
        Assert.Equal(14, config.TextSize);
        Assert.Equal(60, config.BackgroundOpacity);
        Assert.Equal(OverlayColors.DefaultText, config.TextColor);
        Assert.Equal(OverlayMetricKeys.Defaults.ToArray(), config.Metrics.ToArray());
        Assert.Equal("#FFFFD166", config.GroupColors[OverlayMetricKeys.CpuGroup]);
        Assert.Equal("#FF7BD88F", config.GroupColors[OverlayMetricKeys.GpuGroup]);
        Assert.Equal("#FFC792EA", config.GroupColors[OverlayMetricKeys.RamGroup]);
        Assert.Equal("#FF56B6C2", config.GroupColors[OverlayMetricKeys.NetGroup]);
    }

    [Fact]
    public void Defaults_UseVariedStyleWithoutCustomColors()
    {
        var config = new OverlayConfig();

        Assert.Equal(OverlayColors.StyleVaried, config.ColorStyle);
        Assert.False(config.CustomColors);
    }

    [Fact]
    public void VariedStyle_UsesGroupPalette()
    {
        var config = new OverlayConfig { ColorStyle = OverlayColors.StyleVaried };

        Assert.Equal("#FFFFD166", config.ResolveGroupColor(OverlayMetricKeys.CpuGroup));
        Assert.Equal("#FF7BD88F", config.ResolveGroupColor(OverlayMetricKeys.GpuGroup));
    }

    [Fact]
    public void MinimalStyle_UsesOneColorForEveryGroup()
    {
        var config = new OverlayConfig { ColorStyle = OverlayColors.StyleMinimal };

        foreach (string group in OverlayMetricKeys.Groups)
            Assert.Equal(OverlayColors.DefaultText, config.ResolveGroupColor(group));
    }

    [Fact]
    public void StyleIgnoresCustomColorsWhileTheyAreOff()
    {
        var config = new OverlayConfig { ColorStyle = OverlayColors.StyleMinimal, TextColor = "#FF00FF00" };
        config.GroupColors[OverlayMetricKeys.CpuGroup] = "#FFFF0000";

        Assert.Equal(OverlayColors.DefaultText, config.ResolveGroupColor(OverlayMetricKeys.CpuGroup));
    }

    [Fact]
    public void CustomColors_ReplaceTheStyle()
    {
        var config = new OverlayConfig
        {
            ColorStyle = OverlayColors.StyleMinimal,
            CustomColors = true,
            TextColor = "#FF00FF00"
        };
        config.GroupColors[OverlayMetricKeys.CpuGroup] = "#FFFF0000";
        config.GroupColors[OverlayMetricKeys.GpuGroup] = string.Empty;

        Assert.Equal("#FFFF0000", config.ResolveGroupColor(OverlayMetricKeys.CpuGroup));
        Assert.Equal("#FF00FF00", config.ResolveGroupColor(OverlayMetricKeys.GpuGroup));
    }

    [Theory]
    [InlineData("MINIMAL", "minimal")]
    [InlineData("varied", "varied")]
    [InlineData("neon", "varied")]
    [InlineData(null, "varied")]
    public void Normalize_FixesTheColorStyle(string? style, string expected)
    {
        var config = new OverlayConfig { ColorStyle = style! };

        config.Normalize();

        Assert.Equal(expected, config.ColorStyle);
    }

    [Fact]
    public void Normalize_ClampsSizeAndOpacity()
    {
        var config = new OverlayConfig { TextSize = 2, BackgroundOpacity = 140 };

        config.Normalize();

        Assert.Equal(10, config.TextSize);
        Assert.Equal(100, config.BackgroundOpacity);

        config.TextSize = 80;
        config.BackgroundOpacity = -5;
        config.Normalize();

        Assert.Equal(24, config.TextSize);
        Assert.Equal(0, config.BackgroundOpacity);
    }

    [Fact]
    public void Normalize_NormalizesColors()
    {
        var config = new OverlayConfig
        {
            TextColor = "#ffcc00",
            GroupColors = new Dictionary<string, string>
            {
                [OverlayMetricKeys.CpuGroup] = "00FF00",
                [OverlayMetricKeys.GpuGroup] = "no-es-color",
                [OverlayMetricKeys.RamGroup] = " ",
                [OverlayMetricKeys.NetGroup] = "#80112233"
            }
        };

        config.Normalize();

        Assert.Equal("#FFFFCC00", config.TextColor);
        Assert.Equal("#FF00FF00", config.GroupColors[OverlayMetricKeys.CpuGroup]);
        Assert.Equal("#FF7BD88F", config.GroupColors[OverlayMetricKeys.GpuGroup]);
        Assert.Equal(string.Empty, config.GroupColors[OverlayMetricKeys.RamGroup]);
        Assert.Equal("#80112233", config.GroupColors[OverlayMetricKeys.NetGroup]);
    }

    [Fact]
    public void Normalize_InvalidTextColor_FallsBackToWhite()
    {
        var config = new OverlayConfig { TextColor = "#12345" };

        config.Normalize();

        Assert.Equal(OverlayColors.DefaultText, config.TextColor);
    }

    [Fact]
    public void Normalize_FiltersUnknownMetricsAndDuplicates()
    {
        var config = new OverlayConfig
        {
            Metrics = new List<string>
            {
                " CPU.LOAD ", "cpu.load", "gpu.temp", "no.existe", ""
            }
        };

        config.Normalize();

        Assert.Equal(new[] { "cpu.load", "gpu.temp" }, config.Metrics);
    }

    [Fact]
    public void Normalize_UnknownMetricsOnly_RestoresDefaults()
    {
        var config = new OverlayConfig { Metrics = new List<string> { "no.existe", "tampoco" } };

        config.Normalize();

        Assert.Equal(OverlayMetricKeys.Defaults.ToArray(), config.Metrics.ToArray());
    }

    [Fact]
    public void Normalize_EmptySelection_IsRespected()
    {
        var config = new OverlayConfig { Metrics = new List<string>() };

        config.Normalize();

        Assert.Empty(config.Metrics);
    }

    [Fact]
    public void Normalize_NonFinitePosition_BecomesNull()
    {
        var config = new OverlayConfig
        {
            Left = double.NaN,
            Top = double.PositiveInfinity
        };

        config.Normalize();

        Assert.Null(config.Left);
        Assert.Null(config.Top);
    }

    [Fact]
    public void AppConfig_Normalize_CreatesMissingOverlaySection()
    {
        var config = new AppConfig { Overlay = null! };

        config.Normalize();

        Assert.NotNull(config.Overlay);
        Assert.False(config.Overlay.Enabled);
        Assert.Equal(14, config.Overlay.TextSize);
    }
}
