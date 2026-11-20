using System.Globalization;
using CtrlMonitor.Core.Fps;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Tests;

public class FpsTests
{
    // Cabecera real de PresentMon 2.6 con --track_frame_type.
    private const string Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,TimeInMs,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsRenderPresentLatency,MsUntilDisplayed,CPUStartTimeInMs";

    private static string Row(double time, string frameType = "Application", string swapChain = "0x1", bool displayed = true) =>
        string.Join(',',
            "game.exe", "100", swapChain, "DXGI", "0", "0", "0", "Composed: Flip", frameType,
            time.ToString(CultureInfo.InvariantCulture), "NA", "16.6", "16.6", "0.4", "0.5",
            displayed ? "11.0" : "NA", "0");

    private static FpsCalculator Feed(IEnumerable<string> rows)
    {
        var calculator = new FpsCalculator();
        calculator.AddLine(Header);

        foreach (string row in rows)
            calculator.AddLine(row);

        return calculator;
    }

    [Fact]
    public void CountsDisplayedFramesInTheLastSecond()
    {
        // 60 cuadros, uno cada 16,67 ms, durante 2 segundos: el último segundo tiene 60.
        FpsCalculator calculator = Feed(Enumerable.Range(0, 120).Select(i => Row(i * (1000.0 / 60))));

        FpsReading? reading = calculator.Current();

        Assert.NotNull(reading);
        Assert.InRange(reading!.Value.Fps, 59, 61);
        Assert.False(reading.Value.FrameGeneration);
    }

    [Fact]
    public void IgnoresFramesThatWereNotDisplayed()
    {
        FpsCalculator calculator = Feed(Enumerable.Range(0, 100)
            .Select(i => Row(i * 10, displayed: i % 2 == 0)));

        Assert.InRange(calculator.Current()!.Value.Fps, 49, 51);
    }

    [Theory]
    [InlineData("AMD AFMF")]
    [InlineData("Intel XeSS-FG")]
    public void DetectsGeneratedFrames(string generatedType)
    {
        FpsCalculator calculator = Feed(Enumerable.Range(0, 120)
            .Select(i => Row(i * 8.33, i % 2 == 0 ? "Application" : generatedType)));

        FpsReading reading = calculator.Current()!.Value;

        Assert.True(reading.FrameGeneration);
        Assert.InRange(reading.Fps, 119, 121);
    }

    [Fact]
    public void UsesTheBusiestSwapChain()
    {
        IEnumerable<string> game = Enumerable.Range(0, 144).Select(i => Row(i * (1000.0 / 144), swapChain: "0xGAME"));
        IEnumerable<string> launcher = Enumerable.Range(0, 30).Select(i => Row(i * (1000.0 / 30), swapChain: "0xLAUNCHER"));

        FpsCalculator calculator = Feed(game.Concat(launcher).OrderBy(row => double.Parse(row.Split(',')[9], CultureInfo.InvariantCulture)));

        Assert.InRange(calculator.Current()!.Value.Fps, 143, 145);
    }

    [Fact]
    public void WithoutHeaderOrFramesThereIsNoReading()
    {
        var calculator = new FpsCalculator();
        calculator.AddLine(Row(10));

        Assert.False(calculator.HasHeader);
        Assert.Null(calculator.Current());
    }

    [Theory]
    [InlineData("Application", false)]
    [InlineData("NotSet", false)]
    [InlineData("", false)]
    [InlineData("AMD AFMF", true)]
    [InlineData("Intel XeSS-FG", true)]
    public void ClassifiesFrameTypes(string frameType, bool generated) =>
        Assert.Equal(generated, FpsCalculator.IsGenerated(frameType));

    [Fact]
    public void Formatter_ShowsFpsFirst()
    {
        var snapshot = new OverlaySnapshot { Fps = 66.4, CpuLoad = 12 };

        IReadOnlyList<OverlaySegment> segments = OverlayTextFormatter.Format(
            snapshot, new[] { OverlayMetricKeys.CpuLoad, OverlayMetricKeys.Fps }, CultureInfo.InvariantCulture);

        Assert.Equal("FPS 66", segments[0].Text);
        Assert.Equal(OverlayMetricKeys.FpsGroup, segments[0].GroupKey);
        Assert.Equal("CPU 12%", segments[1].Text);
    }

    [Fact]
    public void Formatter_LabelsFrameGenerationAsFg()
    {
        var snapshot = new OverlaySnapshot { Fps = 132, FrameGeneration = true };

        OverlaySegment segment = Assert.Single(OverlayTextFormatter.Format(
            snapshot, new[] { OverlayMetricKeys.Fps }, CultureInfo.InvariantCulture));

        Assert.Equal("FG 132", segment.Text);
    }

    [Fact]
    public void Formatter_ShowsDashesWithoutAGame()
    {
        OverlaySegment segment = Assert.Single(OverlayTextFormatter.Format(
            new OverlaySnapshot(), new[] { OverlayMetricKeys.Fps }, CultureInfo.InvariantCulture));

        Assert.Equal("FPS --", segment.Text);
    }
}
