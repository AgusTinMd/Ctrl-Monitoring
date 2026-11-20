using System.Globalization;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Tests;

public class OverlayTextFormatterTests
{
    private static OverlaySnapshot DefaultSnapshot() => new()
    {
        CpuLoad = 12,
        CpuTemperature = 52,
        CpuClockGhz = 3.98,
        GpuLoad = 91,
        GpuTemperature = 48,
        VramUsedGb = 4.9,
        VramTotalGb = 12.0,
        RamUsedGb = 17.2,
        RamTotalGb = 31.9
    };

    private static string TextOf(IReadOnlyList<OverlaySegment> segments, string group) =>
        segments.Single(segment => segment.GroupKey == group).Text;

    [Fact]
    public void Format_DefaultMetrics_MatchesExpectedBar()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            DefaultSnapshot(),
            OverlayMetricKeys.Defaults,
            CultureInfo.InvariantCulture);

        Assert.Equal(3, segments.Count);
        Assert.Equal("CPU 12% 52°C 3.98GHz", TextOf(segments, OverlayMetricKeys.CpuGroup));
        Assert.Equal("GPU 91% 48°C 4.9/12.0GB", TextOf(segments, OverlayMetricKeys.GpuGroup));
        Assert.Equal("RAM 17.2/31.9GB", TextOf(segments, OverlayMetricKeys.RamGroup));
    }

    [Fact]
    public void Format_AlwaysOrdersCpuGpuRamNet()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { NetDownMbs = 1, RamPercent = 20, GpuLoad = 30, CpuLoad = 40 },
            new[] { OverlayMetricKeys.NetThroughput, OverlayMetricKeys.RamPercent, OverlayMetricKeys.GpuLoad, OverlayMetricKeys.CpuLoad },
            CultureInfo.InvariantCulture);

        Assert.Equal(
            new[]
            {
                OverlayMetricKeys.CpuGroup,
                OverlayMetricKeys.GpuGroup,
                OverlayMetricKeys.RamGroup,
                OverlayMetricKeys.NetGroup
            },
            segments.Select(segment => segment.GroupKey));
    }

    [Fact]
    public void Format_MissingValues_AreOmitted()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { CpuLoad = 12 },
            new[] { OverlayMetricKeys.CpuLoad, OverlayMetricKeys.CpuTemperature, OverlayMetricKeys.CpuClock },
            CultureInfo.InvariantCulture);

        Assert.Equal("CPU 12%", TextOf(segments, OverlayMetricKeys.CpuGroup));
    }

    [Fact]
    public void Format_GroupWithoutValues_ShowsDash()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot(),
            new[] { OverlayMetricKeys.CpuLoad, OverlayMetricKeys.CpuTemperature },
            CultureInfo.InvariantCulture);

        Assert.Equal("CPU --", TextOf(segments, OverlayMetricKeys.CpuGroup));
    }

    [Fact]
    public void Format_NoSelection_ReturnsNoSegments()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            DefaultSnapshot(),
            Array.Empty<string>(),
            CultureInfo.InvariantCulture);

        Assert.Empty(segments);
    }

    [Fact]
    public void Format_UsesConfiguredCulture()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { CpuClockGhz = 3.98, GpuTemperature = 48.6 },
            new[] { OverlayMetricKeys.CpuClock, OverlayMetricKeys.GpuTemperature },
            CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("CPU 3,98GHz", TextOf(segments, OverlayMetricKeys.CpuGroup));
        Assert.Equal("GPU 49°C", TextOf(segments, OverlayMetricKeys.GpuGroup));
    }

    [Fact]
    public void Format_GpuTemperatureAndHotspot_ShareOneReading()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { GpuTemperature = 48, GpuHotspot = 61 },
            new[] { OverlayMetricKeys.GpuTemperature, OverlayMetricKeys.GpuHotspot },
            CultureInfo.InvariantCulture);

        Assert.Equal("GPU 48/61°C", TextOf(segments, OverlayMetricKeys.GpuGroup));
    }

    [Fact]
    public void Format_GpuHotspotOnly_IsShown()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { GpuHotspot = 61 },
            new[] { OverlayMetricKeys.GpuHotspot },
            CultureInfo.InvariantCulture);

        Assert.Equal("GPU 61°C", TextOf(segments, OverlayMetricKeys.GpuGroup));
    }

    [Fact]
    public void Format_Network_ShowsBothDirections()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { NetDownMbs = 1.24, NetUpMbs = 0.31 },
            new[] { OverlayMetricKeys.NetThroughput },
            CultureInfo.InvariantCulture);

        Assert.Equal("NET ↓1.2 ↑0.3MB/s", TextOf(segments, OverlayMetricKeys.NetGroup));
    }

    [Fact]
    public void Format_Network_SingleDirectionIsShown()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { NetUpMbs = 0.31 },
            new[] { OverlayMetricKeys.NetThroughput },
            CultureInfo.InvariantCulture);

        Assert.Equal("NET ↑0.3MB/s", TextOf(segments, OverlayMetricKeys.NetGroup));
    }

    [Fact]
    public void Format_PowerAndFanMetrics_AppendWithUnits()
    {
        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            new OverlaySnapshot { CpuPower = 88, CpuFanRpm = 1200, GpuPower = 220, GpuFanRpm = 1800 },
            new[]
            {
                OverlayMetricKeys.CpuPower, OverlayMetricKeys.CpuFan,
                OverlayMetricKeys.GpuPower, OverlayMetricKeys.GpuFan
            },
            CultureInfo.InvariantCulture);

        Assert.Equal("CPU 88W 1200RPM", TextOf(segments, OverlayMetricKeys.CpuGroup));
        Assert.Equal("GPU 220W 1800RPM", TextOf(segments, OverlayMetricKeys.GpuGroup));
    }
}
