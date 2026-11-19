using System.Diagnostics;
using CtrlMonitor.Core.Processes;

namespace CtrlMonitor.Core.Tests;

public class ProcessTests
{
    [Theory]
    [InlineData(4, "System")]
    [InlineData(0, "Idle")]
    [InlineData(612, "csrss")]
    [InlineData(800, "lsass")]
    [InlineData(700, "WINLOGON")]
    [InlineData(500, "services")]
    public void CriticalProcessesAreProtected(int id, string name) =>
        Assert.True(ProcessKiller.IsProtected(id, name));

    [Theory]
    [InlineData(1234, "notepad")]
    [InlineData(5678, "chrome")]
    [InlineData(4321, "svchost")]
    public void RegularProcessesAreNotProtected(int id, string name) =>
        Assert.False(ProcessKiller.IsProtected(id, name));

    [Fact]
    public void OwnProcessIsProtected() =>
        Assert.True(ProcessKiller.IsProtected(Environment.ProcessId, "testhost"));

    [Fact]
    public void ReadsCurrentProcessWithMemoryAndCpuOnSecondRead()
    {
        var sampler = new ProcessSampler();
        IReadOnlyList<ProcessSample> first = sampler.Read();
        ProcessSample self = Assert.Single(first, sample => sample.Id == Environment.ProcessId);

        Assert.True(self.MemoryBytes > 0);
        Assert.Null(self.CpuPercent);
        Assert.False(string.IsNullOrEmpty(self.Path));

        Thread.Sleep(200);
        ProcessSample again = Assert.Single(sampler.Read(), sample => sample.Id == Environment.ProcessId);
        Assert.NotNull(again.CpuPercent);
    }

    [Fact]
    public void KillsAChildProcess()
    {
        using Process child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;

        long start = child.StartTime.ToUniversalTime().Ticks;

        Assert.Equal(KillResult.Done, ProcessKiller.Kill(child.Id, start, "cmd", entireTree: true));
        Assert.True(child.HasExited);
    }

    [Fact]
    public void DoesNotKillWhenThePidWasReused()
    {
        using Process child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;

        try
        {
            // Otro momento de inicio: para el killer es otro proceso con el mismo PID.
            Assert.Equal(KillResult.NotFound, ProcessKiller.Kill(child.Id, 1, "cmd", entireTree: false));
            Assert.False(child.HasExited);
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }
}
