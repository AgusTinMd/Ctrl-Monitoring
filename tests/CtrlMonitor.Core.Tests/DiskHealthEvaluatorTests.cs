using CtrlMonitor.Core.Hardware;

namespace CtrlMonitor.Core.Tests;

public class DiskHealthEvaluatorTests
{
    private static readonly DiskSmartInfo Healthy = new()
    {
        Number = 0,
        ToolkitStatus = DiskCondition.Good,
        Temperature = 35,
        PowerOnHours = 1000,
        ReallocatedSectors = 0,
        PendingSectors = 0
    };

    [Fact]
    public void HealthyDiskIsGood()
    {
        (DiskCondition condition, IReadOnlyList<DiskIssue> issues) =
            DiskHealthEvaluator.Evaluate(Healthy, DiskCondition.Good);

        Assert.Equal(DiskCondition.Good, condition);
        Assert.Empty(issues);
    }

    [Fact]
    public void ReallocatedSectorsAreCaution()
    {
        (DiskCondition condition, IReadOnlyList<DiskIssue> issues) =
            DiskHealthEvaluator.Evaluate(Healthy with { ReallocatedSectors = 8 }, DiskCondition.Good);

        Assert.Equal(DiskCondition.Caution, condition);
        Assert.Contains(issues, issue => issue.Key == "disks.issue.reallocated" && issue.Value == 8);
    }

    [Fact]
    public void LowLifeIsCaution()
    {
        (DiskCondition condition, _) =
            DiskHealthEvaluator.Evaluate(Healthy with { LifePercent = 6 }, DiskCondition.Good);

        Assert.Equal(DiskCondition.Caution, condition);
    }

    [Fact]
    public void SpareBelowThresholdIsBad()
    {
        (DiskCondition condition, _) = DiskHealthEvaluator.Evaluate(
            Healthy with { AvailableSpare = 5, AvailableSpareThreshold = 10 },
            DiskCondition.Good);

        Assert.Equal(DiskCondition.Bad, condition);
    }

    [Fact]
    public void WorstSourceWins()
    {
        (DiskCondition condition, IReadOnlyList<DiskIssue> issues) =
            DiskHealthEvaluator.Evaluate(Healthy, DiskCondition.Bad);

        Assert.Equal(DiskCondition.Bad, condition);
        Assert.Contains(issues, issue => issue.Key == "disks.issue.windowsBad");
    }

    [Fact]
    public void ToolkitCautionWithoutRuleStillExplains()
    {
        (DiskCondition condition, IReadOnlyList<DiskIssue> issues) = DiskHealthEvaluator.Evaluate(
            Healthy with { ToolkitStatus = DiskCondition.Caution },
            DiskCondition.Good);

        Assert.Equal(DiskCondition.Caution, condition);
        Assert.Contains(issues, issue => issue.Key == "disks.issue.toolkit");
    }

    [Fact]
    public void NoDataIsUnknown()
    {
        (DiskCondition condition, _) = DiskHealthEvaluator.Evaluate(new DiskSmartInfo(), DiskCondition.Unknown);

        Assert.Equal(DiskCondition.Unknown, condition);
    }
}
