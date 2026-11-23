using System.Globalization;
using CtrlMonitor.Core.Fps;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Tests;

public class GraphicsApiTests
{
    [Theory]
    [InlineData("D3D9", new string[0], "DX9")]
    [InlineData("DXGI", new[] { "DX12" }, "DX12")]
    [InlineData("DXGI", new[] { "DX11" }, "DX11")]
    [InlineData("DXGI", new[] { "DX11", "DX12" }, "DX12")]
    [InlineData("DXGI", new[] { "DX11", "VK" }, "VK")]
    [InlineData("Other", new[] { "VK" }, "VK")]
    [InlineData("Other", new[] { "OGL" }, "OGL")]
    [InlineData("Other", new[] { "VK", "OGL" }, "VK")]
    public void Detect_UsesRuntimeAndLoadedLibraries(string runtime, string[] apis, string expected)
    {
        Assert.Equal(expected, GraphicsApi.Detect(runtime, apis));
    }

    [Theory]
    [InlineData("DXGI", new string[0])]
    [InlineData("Other", new string[0])]
    [InlineData(null, new[] { "DX11" })]
    public void Detect_DoesNotGuess(string? runtime, string[] apis)
    {
        Assert.Null(GraphicsApi.Detect(runtime, apis));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\d3d12.dll", true)]
    [InlineData(@"C:\Windows\System32\VULKAN-1.DLL", true)]
    [InlineData(@"C:\Windows\System32\kernel32.dll", false)]
    [InlineData(null, false)]
    public void IsGraphicsModule_MatchesByFileName(string? path, bool expected)
    {
        Assert.Equal(expected, GraphicsApi.IsGraphicsModule(path));
    }

    [Fact]
    public void Formatter_PutsTheApiBeforeTheFps()
    {
        var snapshot = new OverlaySnapshot { Fps = 50, GraphicsApi = "DX9" };

        OverlaySegment segment = Assert.Single(OverlayTextFormatter.Format(
            snapshot, new[] { OverlayMetricKeys.Fps, OverlayMetricKeys.GraphicsApi }, CultureInfo.InvariantCulture));

        Assert.Equal("DX9 FPS 50", segment.Text);
    }

    [Fact]
    public void Formatter_WithoutApiShowsOnlyFps()
    {
        var snapshot = new OverlaySnapshot { Fps = 50 };

        OverlaySegment segment = Assert.Single(OverlayTextFormatter.Format(
            snapshot, new[] { OverlayMetricKeys.Fps, OverlayMetricKeys.GraphicsApi }, CultureInfo.InvariantCulture));

        Assert.Equal("FPS 50", segment.Text);
    }

    [Fact]
    public void GraphicsApi_IsOffByDefault()
    {
        Assert.DoesNotContain(OverlayMetricKeys.GraphicsApi, OverlayMetricKeys.Defaults);
    }
}
