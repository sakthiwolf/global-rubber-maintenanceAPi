using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Rules;

namespace GlobalRubber.MMM.Tests;

/// <summary>The pure usage-based Mold PM rules (interval 100,000, warning margin 10,000 unless stated).</summary>
public class MoldPmRulesTests
{
    private const int Interval = 100_000;
    private const int Warning = 10_000;
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static MoldPmRules.Decision Decide(int usage, int cycleStart = 500_000, int? interval = Interval, int? warning = Warning,
        string status = MoldStatus.InProduction, bool hasOpenPm = false) =>
        MoldPmRules.Decide(interval, cycleStart, warning, usage, status, hasOpenPm);

    [Fact]
    public void NormalUsage_NothingHappens_RemainingIsThresholdMinusUsage()
    {
        Assert.Equal(MoldPmRules.Decision.None, Decide(580_000));
        Assert.Equal(20_000, MoldPmRules.RemainingShots(580_000, 500_000, Interval));
        Assert.Equal(600_000, MoldPmRules.NextThreshold(500_000, Interval));
    }

    [Fact]
    public void InsideTheWarningMargin_RaisesTheWarning()
    {
        var d = Decide(591_500);
        Assert.True(d.RaiseWarning);
        Assert.False(d.CreatePm);
        Assert.Equal(8_500, d.RemainingShots);
    }

    [Fact]
    public void ExactlyAtTheWarningPoint_IsAWarning_OneShotBeforeIsNot()
    {
        Assert.True(Decide(590_000).RaiseWarning);   // remaining 10,000 = margin
        Assert.Equal(MoldPmRules.Decision.None, Decide(589_999));
    }

    [Fact]
    public void ExactlyAtTheThreshold_CreatesThePm_NotAWarning()
    {
        var d = Decide(600_000);
        Assert.True(d.CreatePm);
        Assert.False(d.RaiseWarning);
        Assert.Equal(600_000, d.ThresholdShots);
        Assert.Equal(0, d.RemainingShots);
    }

    [Fact]
    public void ThresholdExceeded_InOneJump_StillCreatesThePm()
    {
        var d = Decide(612_345);
        Assert.True(d.CreatePm);
        Assert.Equal(-12_345, d.RemainingShots);
    }

    [Fact]
    public void WhileAPmIsOpen_NothingHappens_NoBacklog()
    {
        Assert.Equal(MoldPmRules.Decision.None, Decide(610_000, hasOpenPm: true));
        Assert.Equal(MoldPmRules.Decision.None, Decide(750_000, hasOpenPm: true)); // a second threshold passed: still nothing
    }

    [Fact]
    public void RetiredOrDisabled_NothingHappens()
    {
        Assert.Equal(MoldPmRules.Decision.None, Decide(700_000, status: MoldStatus.Retired));
        Assert.Equal(MoldPmRules.Decision.None, Decide(700_000, interval: null));
        Assert.Equal(MoldPmRules.Decision.None, Decide(700_000, interval: 0));
    }

    [Fact]
    public void NoWarningMargin_NeverWarns_ButStillCreatesThePm()
    {
        Assert.Equal(MoldPmRules.Decision.None, Decide(599_999, warning: null));
        Assert.True(Decide(600_000, warning: null).CreatePm);
    }

    [Theory]
    [InlineData(600_000, 600_000, 700_000)] // completed exactly at the threshold
    [InlineData(600_500, 600_000, 700_000)] // a little late
    [InlineData(615_000, 600_000, 700_000)] // the acceptance example: next threshold stays 700,000
    [InlineData(699_999, 600_000, 700_000)]
    [InlineData(700_000, 700_000, 800_000)] // completed at the next threshold: that cycle is covered too
    [InlineData(720_000, 700_000, 800_000)] // missed a whole cycle: no backlog, the original cycle kept
    [InlineData(950_000, 900_000, 1_000_000)]
    public void LateCompletion_KeepsTheOriginalCycle(int usageAtCompletion, int expectedCycleStart, long expectedNextThreshold)
    {
        var cycleStart = MoldPmRules.CycleStartAfterCompletion(600_000, Interval, usageAtCompletion);

        Assert.Equal(expectedCycleStart, cycleStart);
        Assert.Equal(expectedNextThreshold, MoldPmRules.NextThreshold(cycleStart, Interval));
    }

    [Fact]
    public void CompletionAfterADownwardUsageCorrection_NeverRepeatsTheCycle()
    {
        Assert.Equal(600_000, MoldPmRules.CycleStartAfterCompletion(600_000, Interval, 550_000));
    }

    [Fact]
    public void CompletionAtTheAcceptanceExample_Leaves85000Remaining_AndIsNormal()
    {
        var cycleStart = MoldPmRules.CycleStartAfterCompletion(600_000, Interval, 615_000);

        Assert.Equal(85_000, MoldPmRules.RemainingShots(615_000, cycleStart, Interval));
        Assert.Equal(MoldPmState.Normal, MoldPmRules.StateOf(Interval, cycleStart, Warning, 615_000, null, null, Today));
        Assert.Equal(MoldPmState.Warning, MoldPmRules.StateOf(Interval, cycleStart, Warning, 690_000, null, null, Today));
        Assert.True(MoldPmRules.IsDue(700_000, cycleStart, Interval));
    }

    [Fact]
    public void StateOf_OpenPmDecidesFirst()
    {
        Assert.Equal(MoldPmState.InMaintenance, MoldPmRules.StateOf(Interval, 500_000, Warning, 610_000, MoldPmStatus.InProgress, Today.AddDays(-5), Today));
        Assert.Equal(MoldPmState.Due, MoldPmRules.StateOf(Interval, 500_000, Warning, 610_000, MoldPmStatus.Scheduled, Today, Today));
        Assert.Equal(MoldPmState.Overdue, MoldPmRules.StateOf(Interval, 500_000, Warning, 610_000, MoldPmStatus.Scheduled, Today.AddDays(-1), Today));
    }

    [Fact]
    public void StateOf_WithoutAnOpenPm()
    {
        Assert.Equal(MoldPmState.NotConfigured, MoldPmRules.StateOf(null, 0, null, 10, null, null, Today));
        Assert.Equal(MoldPmState.Normal, MoldPmRules.StateOf(Interval, 500_000, Warning, 580_000, null, null, Today));
        Assert.Equal(MoldPmState.Warning, MoldPmRules.StateOf(Interval, 500_000, Warning, 595_000, null, null, Today));
        Assert.Equal(MoldPmState.Due, MoldPmRules.StateOf(Interval, 500_000, Warning, 600_000, null, null, Today)); // e.g. a Retired mold
    }

    [Fact]
    public void MultipleUsageUpdates_OnlyTheCrossingDecisionsChange()
    {
        var decisions = new[] { 580_000, 590_000, 595_000, 599_999, 600_000, 610_000 }
            .Select(u => Decide(u, hasOpenPm: u > 600_000)).ToList();

        Assert.Equal(new[] { false, true, true, true, false, false }, decisions.Select(d => d.RaiseWarning));
        Assert.Equal(new[] { false, false, false, false, true, false }, decisions.Select(d => d.CreatePm));
    }
}
