using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Numeric per-beat samples and derived timing measures (DiffTicTac / DiffPeriod /
/// Avg Period). Expected values follow the worked examples in the TimeGrapher
/// Equations document (beat error 0.8 ms from t1=125.8 ms / t2=124.2 ms @ 28800 bph).
/// </summary>
public sealed class WatchMetricsDerivedMeasuresTests
{
    private const int SampleRate = 48000;
    private const double Bph = 28800.0;           // half-period 125 ms = 6000 samples
    private const double BeatSamples = 6000.0;

    private static WatchMetrics NewMetrics()
    {
        return new WatchMetrics(new WatchMetricsConfig { SampleRate = SampleRate });
    }

    /// <summary>Feeds A events at the given inter-event intervals (ms), returning all updates.</summary>
    private static List<WatchMetricsUpdate> FeedAEvents(WatchMetrics metrics, params double[] intervalsMs)
    {
        var updates = new List<WatchMetricsUpdate>
        {
            metrics.HandleAEvent(0.0, true, Bph),
        };

        double sample = 0.0;
        foreach (double intervalMs in intervalsMs)
        {
            sample += intervalMs / 1000.0 * SampleRate;
            updates.Add(metrics.HandleAEvent(sample, true, Bph));
        }

        return updates;
    }

    [Fact]
    public void SignedBeatErrorAndDiffTicTac_FollowEquationsWorkedExample()
    {
        // t1=125.8 ms (tick), t2=124.2 ms (tock) -> BE = (t1-t2)/2 = +0.8 ms,
        // DiffTicTac = t1-t2 = +1.6 ms.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.8, 124.2);

        WatchMetricsUpdate third = updates[2];
        Assert.True(third.BeatTimingSampleUpdated);
        Assert.True(third.BeatTimingSample.BeatErrorValid);
        Assert.Equal(0.8, third.BeatTimingSample.BeatErrorSignedMs, 6);

        Assert.True(third.DerivedMeasuresUpdated);
        Assert.True(third.DerivedMeasures.DiffTicTacValid);
        Assert.Equal(1.6, third.DerivedMeasures.DiffTicTacMs, 6);
    }

    [Fact]
    public void SignedBeatError_KeepsSignWhenTockIsLonger()
    {
        // Tock longer than tick -> tick-minus-tock is negative.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 124.2, 125.8);

        Assert.Equal(-0.8, updates[2].BeatTimingSample.BeatErrorSignedMs, 6);
        Assert.Equal(-1.6, updates[2].DerivedMeasures.DiffTicTacMs, 6);
    }

    [Fact]
    public void DiffPeriodAndAvgPeriod_AverageMeasuredMinusExpected()
    {
        // Alternating +0.8 / -0.8 ms deltas average to zero.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.8, 124.2);

        DerivedTimingMeasures derived = updates[2].DerivedMeasures;
        Assert.True(derived.DiffPeriodValid);
        Assert.Equal(0.0, derived.DiffPeriodMs, 6);
        Assert.True(derived.AvgPeriodValid);
        Assert.Equal(0.0, derived.AvgPeriodMs, 6);
    }

    [Fact]
    public void DiffPeriodAndAvgPeriod_TrackAConsistentlySlowWatch()
    {
        // Every beat 0.5 ms longer than nominal -> both averages read +0.5 ms.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.5, 125.5, 125.5, 125.5);

        DerivedTimingMeasures derived = updates[4].DerivedMeasures;
        Assert.Equal(0.5, derived.DiffPeriodMs, 6);
        Assert.Equal(0.5, derived.AvgPeriodMs, 6);
    }

    [Fact]
    public void AvgPeriod_ExcludesIntervalsSpanningMissedBeats()
    {
        // A 2.5-beat interval (312.5 ms) is a detection gap, not a beat duration;
        // it must not poison the running averages.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.0, 312.5, 125.0);

        DerivedTimingMeasures derived = updates[3].DerivedMeasures;
        // Only the two exact 125 ms intervals contributed (delta 0 each).
        Assert.True(derived.AvgPeriodValid);
        Assert.Equal(0.0, derived.AvgPeriodMs, 6);
        Assert.Equal(0.0, derived.DiffPeriodMs, 6);
    }

    [Fact]
    public void MissedBeats_CountsBeatsSkippedAcrossDetectionGaps()
    {
        // A 375 ms A-to-A interval spans three nominal 125 ms beats: two beats
        // went undetected.
        WatchMetrics metrics = NewMetrics();
        FeedAEvents(metrics, 125.0, 375.0, 125.0);

        Assert.Equal(2UL, metrics.MissedBeats);
    }

    [Fact]
    public void MissedBeats_IgnoresSpuriouslyShortIntervals()
    {
        // A too-short interval is a spurious extra detection, not a missed beat.
        WatchMetrics metrics = NewMetrics();
        FeedAEvents(metrics, 125.0, 30.0, 125.0);

        Assert.Equal(0UL, metrics.MissedBeats);
    }

    [Fact]
    public void BeatTimingSample_CarriesBeatNumberPhaseAndRateError()
    {
        // Exact nominal intervals: the zero-offset anchor makes every rate error 0.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.0, 125.0);

        Assert.True(updates[0].BeatTimingSampleUpdated);
        Assert.Equal(1UL, updates[0].BeatTimingSample.BeatNumber);
        Assert.True(updates[0].BeatTimingSample.IsTic);
        Assert.Equal(0.0, updates[0].BeatTimingSample.TimeS, 9);

        Assert.Equal(2UL, updates[1].BeatTimingSample.BeatNumber);
        Assert.False(updates[1].BeatTimingSample.IsTic);
        Assert.Equal(BeatSamples / SampleRate, updates[1].BeatTimingSample.TimeS, 9);
        Assert.Equal(0.0, updates[1].BeatTimingSample.RateErrorMs, 6);

        Assert.True(updates[2].BeatTimingSample.IsTic);
    }

    [Fact]
    public void BeatTimingSample_NotEmittedWithoutValidBph()
    {
        WatchMetrics metrics = NewMetrics();
        WatchMetricsUpdate update = metrics.HandleAEvent(0.0, haveValidBph: false, bph: 0.0);

        Assert.False(update.BeatTimingSampleUpdated);
        Assert.False(update.DerivedMeasuresUpdated);
    }

    [Fact]
    public void AmplitudeSample_EmittedPerCEvent_AndPairAverageOncePerTicTocPair()
    {
        // bph=3600: half-period 1 s. A->C interval of 1/6 s puts the sine argument at
        // pi/6, so Amplitude = liftAngle / sin(pi/6) = 2 * 52 = 104 degrees.
        const double bph = 3600.0;
        const double aToCSamples = SampleRate / 6.0;
        WatchMetrics metrics = NewMetrics();

        metrics.HandleAEvent(0.0, true, bph);                     // beat 1 (tic)
        WatchMetricsUpdate ticC = metrics.HandleCEvent(aToCSamples, true, bph);

        Assert.True(ticC.AmplitudeSampleUpdated);
        Assert.True(ticC.AmplitudeSample.InstantValid);
        Assert.Equal(104.0, ticC.AmplitudeSample.InstantDeg, 6);
        Assert.False(ticC.AmplitudeSample.PairAverageUpdated);

        double tocA = SampleRate * 1.0;                           // beat 2 (toc), 1 s later
        metrics.HandleAEvent(tocA, true, bph);
        WatchMetricsUpdate tocC = metrics.HandleCEvent(tocA + aToCSamples, true, bph);

        Assert.True(tocC.AmplitudeSample.PairAverageUpdated);
        Assert.Equal(104.0, tocC.AmplitudeSample.PairAverageDeg, 6);
    }

    [Fact]
    public void AmplitudeSample_NotEmittedWhenEstimateIsOutOfRange()
    {
        // A->C of a quarter period at bph=3600 gives sin(pi/2)=1 -> 52 deg; shrink the
        // interval until the estimate exceeds 360 and the sample must be suppressed.
        const double bph = 3600.0;
        WatchMetrics metrics = NewMetrics();
        metrics.HandleAEvent(0.0, true, bph);

        // sin arg ~ 0.04 rad -> amplitude ~ 1300 deg (> 360): invalid.
        WatchMetricsUpdate update = metrics.HandleCEvent(600.0, true, bph);

        Assert.False(update.AmplitudeSampleUpdated);
    }
}
