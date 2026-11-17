using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.Core.Tests;

public class ScheduledTaskXmlTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Actions Context="Author">
            <Exec>
              <Command>"C:\Program Files\Ctrl Monitor\CtrlMonitor.exe"</Command>
              <Arguments>--minimized</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    [Fact]
    public void GetCommand_ReturnsPathWithoutQuotes()
    {
        Assert.Equal(@"C:\Program Files\Ctrl Monitor\CtrlMonitor.exe", ScheduledTaskXml.GetCommand(Xml));
    }

    [Fact]
    public void GetCommand_DecodesXmlEntities()
    {
        const string xml = "<Command>C:\\Apps &amp; Tools\\CtrlMonitor.exe</Command>";

        Assert.Equal(@"C:\Apps & Tools\CtrlMonitor.exe", ScheduledTaskXml.GetCommand(xml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<Task><Actions /></Task>")]
    public void GetCommand_WithoutCommand_ReturnsNull(string? xml)
    {
        Assert.Null(ScheduledTaskXml.GetCommand(xml));
    }

    [Fact]
    public void PointsTo_SamePathIgnoringCase()
    {
        Assert.True(ScheduledTaskXml.PointsTo(Xml, @"c:\program files\ctrl monitor\CTRLMONITOR.EXE"));
    }

    [Fact]
    public void PointsTo_OtherFolder_IsFalse()
    {
        Assert.False(ScheduledTaskXml.PointsTo(Xml, @"E:\Ctrl Monitor\CtrlMonitor.exe"));
    }

    [Fact]
    public void PointsTo_NoCommand_IsFalse()
    {
        Assert.False(ScheduledTaskXml.PointsTo("<Task />", @"E:\Ctrl Monitor\CtrlMonitor.exe"));
    }
}
