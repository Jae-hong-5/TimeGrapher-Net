using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Rayleigh phase-score period detection (Bph.PhaseScore / Bph.PickByPhase):
/// the score concentrates to 1 for perfectly periodic events, the 0.7*median-AA
/// floor rejects a 2x-alias (half-period) candidate, and the minScore gate
/// returns 0 when no candidate is concentrated enough.
/// </summary>
public sealed class BphPhaseTests
{
    private static double[] PeriodicEvents(double period, int count)
    {
        var events = new double[count];
        for (int i = 0; i < count; i++)
        {
            events[i] = i * period;
        }

        return events;
    }

    [Fact]
    public void PhaseScore_ReturnsZero_ForTooFewEventsOrNonPositivePeriod()
    {
        double[] events = PeriodicEvents(1.0 / 6.0, count: 5);

        // n < 6: the Rayleigh statistic needs at least 6 events.
        Assert.Equal(0.0, Bph.PhaseScore(events, events.Length, 1.0 / 6.0));

        double[] enough = PeriodicEvents(1.0 / 6.0, count: 8);
        // period <= 0 is rejected outright.
        Assert.Equal(0.0, Bph.PhaseScore(enough, enough.Length, 0.0));
        Assert.Equal(0.0, Bph.PhaseScore(enough, enough.Length, -1.0));
    }

    [Fact]
    public void PhaseScore_NearOne_ForPerfectlyPeriodicEvents()
    {
        const double period = 1.0 / 6.0; // 21600 BPH
        double[] events = PeriodicEvents(period, count: 30);

        double score = Bph.PhaseScore(events, events.Length, period);

        Assert.True(score > 0.99, $"expected concentrated phase score, got {score}");
    }

    [Fact]
    public void PickByPhase_RejectsHalfPeriodAlias_AndReturnsTrueBph()
    {
        const int trueBph = 21600;  // T = 1/6 s
        const int twoX = 43200;     // T = 1/12 s, below the 0.7*median-AA floor
        const double period = 3600.0 / trueBph;
        double[] events = PeriodicEvents(period, count: 30);
        int[] list = { twoX, trueBph };

        int picked = Bph.PickByPhase(events, events.Length, list, list.Length, minScore: 0.5,
            out double outScore, out double outPeriod);

        // The 2x candidate (half period) is implausibly short and skipped, so the true
        // BPH wins even though it is listed second.
        Assert.Equal(trueBph, picked);
        Assert.True(outScore > 0.99, $"expected concentrated phase score, got {outScore}");
        Assert.Equal(period, outPeriod, precision: 9);
    }

    [Fact]
    public void PickByPhase_ReturnsZero_WhenBestScoreBelowMinScore()
    {
        const int trueBph = 21600;
        const double period = 3600.0 / trueBph;
        // Alternating +/- quarter-period offsets de-concentrate the folded phases, so the
        // Rayleigh score stays well below a strict 0.99 gate.
        var events = new double[30];
        for (int i = 0; i < events.Length; i++)
        {
            events[i] = i * period + (i % 2 == 0 ? 0.0 : period * 0.25);
        }
        int[] list = { trueBph };

        int picked = Bph.PickByPhase(events, events.Length, list, list.Length, minScore: 0.99,
            out double outScore, out _);

        Assert.Equal(0, picked);
        Assert.True(outScore < 0.99, $"expected a sub-threshold score, got {outScore}");
    }
}
