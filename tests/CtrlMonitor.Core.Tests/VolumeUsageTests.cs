using CtrlMonitor.Core.Inventory;

namespace CtrlMonitor.Core.Tests;

public class VolumeUsageTests
{
    [Fact]
    public void UsedIsTotalMinusFree()
    {
        var volume = new VolumeUsage("C:", "Windows", 500, 125);

        Assert.Equal(375, volume.UsedBytes);
        Assert.Equal(75, volume.UsedPercent, 3);
    }

    [Fact]
    public void EmptyVolumeDoesNotDivideByZero()
    {
        Assert.Equal(0, new VolumeUsage("X:", "", 0, 0).UsedPercent);
    }

    [Fact]
    public void ReadsAtLeastTheSystemDrive()
    {
        IReadOnlyList<VolumeUsage> volumes = VolumeUsageReader.Read();
        string system = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');

        Assert.Contains(volumes, volume => volume.Letter.Equals(system, StringComparison.OrdinalIgnoreCase));
    }
}
