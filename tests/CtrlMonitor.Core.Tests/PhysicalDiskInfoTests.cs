using CtrlMonitor.Core.Inventory;

namespace CtrlMonitor.Core.Tests;

public class PhysicalDiskInfoTests
{
    [Theory]
    [InlineData(3, "HDD")]
    [InlineData(4, "SSD")]
    [InlineData(0, "")]
    public void MapsMediaType(int value, string expected) =>
        Assert.Equal(expected, PhysicalDiskReader.MediaTypeName(value));

    [Theory]
    [InlineData(17, "NVMe")]
    [InlineData(11, "SATA")]
    [InlineData(7, "USB")]
    [InlineData(99, "")]
    public void MapsBusType(int value, string expected) =>
        Assert.Equal(expected, PhysicalDiskReader.BusTypeName(value));

    [Theory]
    [InlineData(0, DiskHealth.Healthy)]
    [InlineData(1, DiskHealth.Warning)]
    [InlineData(2, DiskHealth.Unhealthy)]
    [InlineData(5, DiskHealth.Unknown)]
    public void MapsHealth(int value, DiskHealth expected) =>
        Assert.Equal(expected, PhysicalDiskReader.HealthFrom(value));

    [Fact]
    public void ReadsDisksWithLetters()
    {
        IReadOnlyList<PhysicalDiskInfo> disks = PhysicalDiskReader.Read();

        Assert.NotEmpty(disks);
        string system = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        Assert.Contains(disks, disk => disk.Letters.Contains(system, StringComparer.OrdinalIgnoreCase));
    }
}
