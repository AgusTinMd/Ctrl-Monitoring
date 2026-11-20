using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.Core.Tests;

public class OverlayAnchorsTests
{
    private static readonly OverlayRect Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(OverlayAnchors.TopLeft, 0, 0)]
    [InlineData(OverlayAnchors.TopCenter, 860, 0)]
    [InlineData(OverlayAnchors.TopRight, 1720, 0)]
    [InlineData(OverlayAnchors.MiddleLeft, 0, 525)]
    [InlineData(OverlayAnchors.MiddleCenter, 860, 525)]
    [InlineData(OverlayAnchors.BottomRight, 1720, 1050)]
    public void Place_PinsTheBarToTheChosenSpot(string anchor, double left, double top)
    {
        (double x, double y) = OverlayAnchors.Place(anchor, Screen, 200, 30);

        Assert.Equal(left, x);
        Assert.Equal(top, y);
    }

    [Fact]
    public void Place_RightAnchorGrowsToTheLeft()
    {
        (double narrow, _) = OverlayAnchors.Place(OverlayAnchors.TopRight, Screen, 200, 30);
        (double wide, _) = OverlayAnchors.Place(OverlayAnchors.TopRight, Screen, 500, 30);

        Assert.Equal(1920, narrow + 200);
        Assert.Equal(1920, wide + 500);
    }

    [Fact]
    public void Place_WorksOnASecondMonitor()
    {
        var second = new OverlayRect(1920, 0, 1280, 1024);

        (double x, double y) = OverlayAnchors.Place(OverlayAnchors.BottomLeft, second, 200, 30);

        Assert.Equal(1920, x);
        Assert.Equal(994, y);
    }

    [Fact]
    public void Snap_NearACornerBecomesThatAnchor()
    {
        string anchor = OverlayAnchors.Snap(Screen, 1712, 9, 200, 30, 18, out double left, out double top);

        Assert.Equal(OverlayAnchors.TopRight, anchor);
        Assert.Equal(1720, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void Snap_NearTheCenterBecomesCenter()
    {
        string anchor = OverlayAnchors.Snap(Screen, 852, 530, 200, 30, 18, out _, out _);

        Assert.Equal(OverlayAnchors.MiddleCenter, anchor);
    }

    [Fact]
    public void Snap_AlignsOneAxisAndKeepsTheOtherFree()
    {
        string anchor = OverlayAnchors.Snap(Screen, 400, 6, 200, 30, 18, out double left, out double top);

        Assert.Equal(OverlayAnchors.Custom, anchor);
        Assert.Equal(400, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void Snap_FarFromGuidesStaysFree()
    {
        string anchor = OverlayAnchors.Snap(Screen, 300, 300, 200, 30, 18, out double left, out double top);

        Assert.Equal(OverlayAnchors.Custom, anchor);
        Assert.Equal(300, left);
        Assert.Equal(300, top);
    }

    [Fact]
    public void Config_DefaultsToTopCenterWithRoundCorners()
    {
        var config = new OverlayConfig();

        Assert.Equal(OverlayAnchors.TopCenter, config.Anchor);
        Assert.False(config.SquareCorners);
    }

    [Theory]
    [InlineData("TOP-RIGHT", "top-right")]
    [InlineData("nowhere", "top-center")]
    [InlineData(null, "top-center")]
    public void Normalize_FixesTheAnchor(string? anchor, string expected)
    {
        var config = new OverlayConfig { Anchor = anchor! };

        config.Normalize();

        Assert.Equal(expected, config.Anchor);
    }

    [Fact]
    public void Normalize_CustomWithoutCoordinatesFallsBackToTheDefault()
    {
        var config = new OverlayConfig { Anchor = OverlayAnchors.Custom };

        config.Normalize();

        Assert.Equal(OverlayAnchors.Default, config.Anchor);
    }

    [Fact]
    public void Normalize_KeepsCustomWithCoordinates()
    {
        var config = new OverlayConfig { Anchor = OverlayAnchors.Custom, Left = 120, Top = 40 };

        config.Normalize();

        Assert.Equal(OverlayAnchors.Custom, config.Anchor);
        Assert.Equal(120, config.Left);
    }
}
