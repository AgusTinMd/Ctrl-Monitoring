using CtrlMonitor.Core.Inventory;

namespace CtrlMonitor.Core.Tests;

public class DiskNamesTests
{
    [Theory]
    [InlineData("0 D:", 0, "D:")]
    [InlineData("2 C:", 2, "C:")]
    [InlineData("1 E: F:", 1, "E: F:")]
    [InlineData("3", 3, "")]
    [InlineData("Unknown", -1, "Unknown")]
    public void ParsesInstanceNames(string id, int number, string letters)
    {
        Assert.Equal(number, DiskNames.Number(id));
        Assert.Equal(letters, DiskNames.Letters(id));
    }
}
