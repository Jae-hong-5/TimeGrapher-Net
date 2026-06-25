using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure cross-position consistency verdict: the OK/CHECK/COLLECTING gate
/// (active position qualified, then ≥3 qualified positions with the 2V+1H
/// balance-wheel requirement, then the 15 s/d rate-spread grade) plus the
/// per-requirement statuses, extracted so Health and Positions share one rule.
/// </summary>
public sealed class ConsistencyDiagnosisTests
{
    private static StatsSummary Stats(double mean, long count) =>
        new(Valid: true, Min: mean, Max: mean, Mean: mean, Sigma: 0.0, Count: count);

    private static PositionSummary Position(WatchPosition position, double rate, long count) =>
        new(position, Stats(rate, count), default, default);

    private static ConsistencyDiagnosis Diagnose(WatchPosition active, params PositionSummary[] positions) =>
        ConsistencyDiagnosis.Compute(SequenceSummary.Compute(positions), active);

    [Fact]
    public void Compute_CollectingWhileActivePositionIsBelowVerdictBeats()
    {
        ConsistencyDiagnosis d = Diagnose(WatchPosition.CH, Position(WatchPosition.CH, rate: 0.0, count: 10));

        Assert.Equal(VarioVerdictLevel.Pending, d.Level);
        Assert.Equal("COLLECTING", d.VerdictText);
        Assert.Equal("Measuring CH: 10/30 beats.", d.DetailText);
    }

    [Fact]
    public void Compute_CollectingUntilThreePositionsQualify()
    {
        // Active position qualified, but only one position has enough beats.
        ConsistencyDiagnosis d = Diagnose(WatchPosition.CH, Position(WatchPosition.CH, rate: 0.0, count: 30));

        Assert.Equal(VarioVerdictLevel.Pending, d.Level);
        Assert.Equal("COLLECTING", d.VerdictText);
        Assert.Equal(ConsistencyStatus.Collecting, d.SpreadStatus);
    }

    [Fact]
    public void Compute_CollectingUntilBalanceWheelRequirementMet()
    {
        // Three qualified positions but only one full vertical (P3H): the
        // 2-vertical balance-wheel requirement is unmet, so no OK/CHECK yet.
        ConsistencyDiagnosis d = Diagnose(
            WatchPosition.CH,
            Position(WatchPosition.CH, rate: 0.0, count: 30),
            Position(WatchPosition.CB, rate: 2.0, count: 30),
            Position(WatchPosition.P3H, rate: 4.0, count: 30));

        Assert.Equal(VarioVerdictLevel.Pending, d.Level);
        Assert.Equal("Measure full vertical and horizontal positions.", d.DetailText);
        Assert.Equal(ConsistencyStatus.Collecting, d.BalanceStatus);
    }

    [Fact]
    public void Compute_CheckWhenRateSpreadExceedsThreshold()
    {
        // 2V (P3H, P9H) + 1H (CH), vertical rate spread 20 s/d > 15 s/d.
        ConsistencyDiagnosis d = Diagnose(
            WatchPosition.CH,
            Position(WatchPosition.CH, rate: 0.0, count: 30),
            Position(WatchPosition.P3H, rate: 0.0, count: 30),
            Position(WatchPosition.P9H, rate: 20.0, count: 30));

        Assert.Equal(VarioVerdictLevel.Warn, d.Level);
        Assert.Equal("CHECK", d.VerdictText);
        Assert.Equal(ConsistencyStatus.Check, d.SpreadStatus);
        Assert.Equal(ConsistencyStatus.Check, d.BalanceStatus);
        Assert.Equal(ConsistencyStatus.Ready, d.VerticalHorizontalStatus);
        Assert.Equal(20.0, d.RateSpreadSPerDay!.Value, 9);
    }

    [Fact]
    public void Compute_OkWhenRateSpreadWithinThreshold()
    {
        ConsistencyDiagnosis d = Diagnose(
            WatchPosition.CH,
            Position(WatchPosition.CH, rate: 0.0, count: 30),
            Position(WatchPosition.P3H, rate: 2.0, count: 30),
            Position(WatchPosition.P9H, rate: 5.0, count: 30));

        Assert.Equal(VarioVerdictLevel.Good, d.Level);
        Assert.Equal("OK", d.VerdictText);
        Assert.Equal(ConsistencyStatus.Ok, d.SpreadStatus);
        Assert.Equal(ConsistencyStatus.Ok, d.BalanceStatus);
        Assert.Equal(ConsistencyStatus.Ready, d.VerticalHorizontalStatus);
    }
}
