using FanCtrl.Core.Charting;

namespace FanCtrl.Core.Tests;

public class AxisTicksTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(5, 10)]
    [InlineData(10, 20)]
    [InlineData(0.5, 1)]
    public void NextNice_FollowsOneTwoFiveSeries(double value, double expected)
    {
        Assert.Equal(expected, AxisTicks.NextNice(value), 9);
    }

    [Fact]
    public void Network_LargeRange_DoesNotCrowdLabels()
    {
        // Caso de la captura: descarga ~60 MB/s, paso base 1, tarjeta baja.
        const double height = 200;
        AxisTicks ticks = AxisTicks.Compute(0, 61, 1, height);

        Assert.True(61 / ticks.GridStep <= height / AxisTicks.MinGridSpacing);
        Assert.True(61 / ticks.LabelStep <= height / AxisTicks.MinLabelSpacing);
        Assert.Equal(5, ticks.GridStep);
        Assert.Equal(10, ticks.LabelStep);
    }

    [Fact]
    public void Network_FullGigabit_StaysReadable()
    {
        AxisTicks ticks = AxisTicks.Compute(0, 120, 1, 140);

        Assert.True(120 / ticks.LabelStep <= 140 / AxisTicks.MinLabelSpacing);
    }

    [Fact]
    public void Temperature_KeepsPreviousLook()
    {
        // 20–100 °C con paso 10: líneas cada 10 y etiquetas cada 20, como antes.
        AxisTicks ticks = AxisTicks.Compute(20, 100, 10, 200);

        Assert.Equal(10, ticks.GridStep);
        Assert.Equal(20, ticks.LabelStep);
    }

    [Fact]
    public void LabelStep_IsMultipleOfGridStep()
    {
        for (double range = 3; range < 2000; range *= 1.37)
        {
            AxisTicks ticks = AxisTicks.Compute(0, range, 1, 180);
            double ratio = ticks.LabelStep / ticks.GridStep;

            Assert.Equal(Math.Round(ratio), ratio, 9);
        }
    }

    [Fact]
    public void DegenerateInput_FallsBackToBaseStep()
    {
        Assert.Equal(new AxisTicks(10, 10), AxisTicks.Compute(50, 50, 10, 200));
        Assert.Equal(new AxisTicks(1, 1), AxisTicks.Compute(0, 10, 0, 0));
    }
}
