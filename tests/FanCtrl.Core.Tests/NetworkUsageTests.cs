using FanCtrl.Core.Inventory;

namespace FanCtrl.Core.Tests;

public class NetworkUsageTests
{
    [Fact]
    public void MegaBytesPerSecond_ConvertsFromBytes()
    {
        var usage = new NetworkUsage(1024 * 1024, 2.5 * 1024 * 1024);

        Assert.Equal(1, usage.DownMegaBytesPerSecond, 6);
        Assert.Equal(2.5, usage.UpMegaBytesPerSecond, 6);
    }

    [Fact]
    public void Zero_HasNoTraffic()
    {
        Assert.Equal(0, NetworkUsage.Zero.DownBytesPerSecond);
        Assert.Equal(0, NetworkUsage.Zero.UpBytesPerSecond);
    }
}
