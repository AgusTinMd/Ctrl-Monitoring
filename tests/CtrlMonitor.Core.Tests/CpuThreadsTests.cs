using CtrlMonitor.Core.Hardware;

namespace CtrlMonitor.Core.Tests;

public class CpuThreadsTests
{
    private static SensorReading Load(string name, double value) =>
        new(name, "cpu", "CPU", "Cpu", name, SensorKind.Load, 0, value, "%");

    private static SensorReading Temp(string name, double value) =>
        new(name, "cpu", "CPU", "Cpu", name, SensorKind.Temperature, 0, value, "°C");

    [Fact]
    public void UsesThreadSensors_InCoreAndThreadOrder()
    {
        var sensors = new[]
        {
            Load("CPU Total", 30),
            Load("CPU Core Max", 90),
            Load("CPU Core #2 Thread #1", 20),
            Load("CPU Core #1 Thread #2", 15),
            Load("CPU Core #1 Thread #1", 10),
            Load("CPU Core #2 Thread #2", 25),
            Load("CPU Core #1", 12),
            Temp("CPU Core #1", 50)
        };

        IReadOnlyList<CpuThreadLoad> threads = CpuThreads.From(sensors);

        Assert.Equal(new[] { (1, 1), (1, 2), (2, 1), (2, 2) }, threads.Select(t => (t.Core, t.Thread)));
        Assert.Equal(new[] { 10.0, 15, 20, 25 }, threads.Select(t => t.Load));
    }

    [Fact]
    public void FallsBackToCores_WhenThereAreNoThreadSensors()
    {
        var sensors = new[] { Load("CPU Core #2", 40), Load("CPU Core #1", 60), Load("CPU Total", 50) };

        IReadOnlyList<CpuThreadLoad> threads = CpuThreads.From(sensors);

        Assert.Equal(new[] { 1, 2 }, threads.Select(t => t.Core));
        Assert.Equal(new[] { 60.0, 40 }, threads.Select(t => t.Load));
    }

    [Fact]
    public void ClampsLoadAndIgnoresOtherSensors()
    {
        var sensors = new[] { Load("CPU Core #1 Thread #1", 140), Load("GPU Core", 50) };

        CpuThreadLoad only = Assert.Single(CpuThreads.From(sensors));

        Assert.Equal(100, only.Load);
    }

    [Fact]
    public void NoCpuSensors_GivesEmptyList()
    {
        Assert.Empty(CpuThreads.From(Array.Empty<SensorReading>()));
    }
}
