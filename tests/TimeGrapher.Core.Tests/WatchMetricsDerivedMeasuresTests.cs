using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Numeric per-beat samples and derived timing measures (DiffTicTac / DiffPeriod /
/// Avg Period). Expected values follow the worked examples in the TimeGrapher
/// Equations document (beat error 0.8 ms from t1=125.8 ms / t2=124.2 ms @ 28800 BPH).
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
        return FeedAEventsAtBph(metrics, Bph, intervalsMs);
    }

    private static List<WatchMetricsUpdate> FeedAEventsAtBph(WatchMetrics metrics, double bph, params double[] intervalsMs)
    {
        var updates = new List<WatchMetricsUpdate>
        {
            metrics.HandleAEvent(0.0, true, bph),
        };

        double sample = 0.0;
        foreach (double intervalMs in intervalsMs)
        {
            sample += intervalMs / 1000.0 * SampleRate;
            updates.Add(metrics.HandleAEvent(sample, true, bph));
        }

        return updates;
    }

    private static string FeedAThenCAndGetResults(
        WatchMetrics metrics,
        double aSample,
        double cOffsetMs = 50.0)
    {
        metrics.HandleAEvent(aSample, true, Bph);
        WatchMetricsUpdate cUpdate = metrics.HandleCEvent(
            aSample + cOffsetMs / 1000.0 * SampleRate,
            true,
            Bph);
        Assert.True(cUpdate.ResultsUpdated);
        return cUpdate.ResultsText;
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
    public void SignedBeatError_NotContaminatedByStaleWindowAcrossBphRelock()
    {
        // Lock at 18000 BPH (200 ms beats), then re-lock at 19800 BPH (181.8 ms)
        // where the transition (last 18000 beat -> first 19800 beat) is still
        // ~200 ms. Before the fix, the re-lock branch reset the RLS/rolling state
        // but not the beat-error window, so the stale 200 ms boundary interval
        // (which still passes IsSingleBeatInterval at 19800) combined with a new
        // 181.8 ms interval to validate a false ~9 ms signed beat error.
        WatchMetrics metrics = NewMetrics();
        FeedAEventsAtBph(metrics, 18000.0, 200.0, 200.0, 200.0, 200.0);

        double period198Ms = 3600.0 / 19800.0 * 1000.0; // 181.818 ms
        var relock = new List<WatchMetricsUpdate>();
        double sample = 0.8 * SampleRate; // end of the 18000 segment (4 x 200 ms)
        foreach (double intervalMs in new[] { 200.0, period198Ms, period198Ms, period198Ms })
        {
            sample += intervalMs / 1000.0 * SampleRate;
            relock.Add(metrics.HandleAEvent(sample, true, 19800.0));
        }

        // Equal 19800 beats give a true signed beat error of ~0; no post-relock
        // window may surface the ~9 ms stale-boundary artifact.
        foreach (WatchMetricsUpdate u in relock)
        {
            if (u.BeatTimingSampleUpdated && u.BeatTimingSample.BeatErrorValid)
            {
                Assert.True(
                    Math.Abs(u.BeatTimingSample.BeatErrorSignedMs) < 1.0,
                    $"stale beat-error window leaked across BPH re-lock: {u.BeatTimingSample.BeatErrorSignedMs} ms");
            }
        }
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
    public void DiffPeriodWindow_RoundsNonIntegerBeatsPerSecond()
    {
        const double bph = 19800.0;
        double expectedMs = 3600.0 / bph * 1000.0;
        double[] intervals = Enumerable.Range(1, 21)
            .Select(deltaMs => expectedMs + deltaMs)
            .ToArray();
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEventsAtBph(metrics, bph, intervals);

        DerivedTimingMeasures derived = updates[updates.Count - 1].DerivedMeasures;
        Assert.True(derived.DiffPeriodValid);
        Assert.Equal(11.0, derived.DiffPeriodMs, 6);
        Assert.Equal(11.0, derived.AvgPeriodMs, 6);
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
    public void SignedBeatError_InvalidatedAcrossDetectionGaps()
    {
        // A 375 ms window interval spans a detection gap; without the guard the
        // window would emit (0.375-0.125)/2 = +125 ms/2 = a ~62.5 ms fake error.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.0, 375.0, 125.0, 125.0);

        // Window [0,125,500] completed with the gap interval: signed values invalid.
        Assert.False(updates[2].BeatTimingSample.BeatErrorValid);
        Assert.False(updates[2].DerivedMeasures.DiffTicTacValid);

        // No completion on the 4th event; still invalid.
        Assert.False(updates[3].BeatTimingSample.BeatErrorValid);

        // The next clean window [500,625,750] restores valid signed values.
        Assert.True(updates[4].BeatTimingSample.BeatErrorValid);
        Assert.Equal(0.0, updates[4].BeatTimingSample.BeatErrorSignedMs, 6);
        Assert.True(updates[4].DerivedMeasures.DiffTicTacValid);
    }

    [Fact]
    public void DisplayBeatError_ExcludesGapSpanningWindows()
    {
        WatchMetrics metrics = NewMetrics();
        double sample = 0.0;

        string results = FeedAThenCAndGetResults(metrics, sample);
        sample += 125.0 / 1000.0 * SampleRate;
        results = FeedAThenCAndGetResults(metrics, sample);
        sample += 375.0 / 1000.0 * SampleRate;
        results = FeedAThenCAndGetResults(metrics, sample);

        Assert.Equal(
            $"Error Rate ------ s/d | Amplitude {WatchMetrics.ValueSpanStart} 55{WatchMetrics.ValueSpanEnd}° | BEAT ERROR ---- ms | BPH {WatchMetrics.ValueSpanStart}28800{WatchMetrics.ValueSpanEnd}",
            results);

        sample += 125.0 / 1000.0 * SampleRate;
        results = FeedAThenCAndGetResults(metrics, sample);
        sample += 125.0 / 1000.0 * SampleRate;
        results = FeedAThenCAndGetResults(metrics, sample);

        Assert.Equal(
            $"Error Rate ------ s/d | Amplitude {WatchMetrics.ValueSpanStart} 55{WatchMetrics.ValueSpanEnd}° | BEAT ERROR {WatchMetrics.ValueSpanStart} 0.0{WatchMetrics.ValueSpanEnd} ms | BPH {WatchMetrics.ValueSpanStart}28800{WatchMetrics.ValueSpanEnd}",
            results);
    }

    [Fact]
    public void DiffTicTac_KeepsSignAcrossAnOddDetectionGap()
    {
        // Tick 125.8 ms / tock 124.2 ms; one toc goes undetected mid-run (the
        // 250.0 ms interval spans it). Re-anchoring the beat counter keeps the
        // post-gap tic/toc labels aligned with the physical phase, so the
        // signed measures keep their +0.8/+1.6 ms sign instead of inverting.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(
            metrics, 125.8, 124.2, 125.8, 124.2, 250.0, 125.8, 124.2, 125.8);

        WatchMetricsUpdate postGap = updates[8];
        Assert.True(postGap.BeatTimingSample.BeatErrorValid);
        Assert.Equal(0.8, postGap.BeatTimingSample.BeatErrorSignedMs, 6);
        Assert.True(postGap.DerivedMeasures.DiffTicTacValid);
        Assert.Equal(1.6, postGap.DerivedMeasures.DiffTicTacMs, 6);
    }

    [Fact]
    public void BeatCounter_SkipsUndetectedBeatsAcrossAGap()
    {
        // One beat undetected: the next sample is physical beat 4 (toc), not
        // beat 3, and the expected-time schedule stays anchored, so a watch on
        // its nominal schedule still reads a zero Error Rate after the gap.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.0, 250.0, 125.0);

        Assert.Equal(4UL, updates[2].BeatTimingSample.BeatNumber);
        Assert.False(updates[2].BeatTimingSample.IsTic);
        Assert.Equal(0.0, updates[2].BeatTimingSample.RateErrorMs, 6);
        Assert.Equal(5UL, updates[3].BeatTimingSample.BeatNumber);
        Assert.True(updates[3].BeatTimingSample.IsTic);
        Assert.Equal(0.0, updates[3].BeatTimingSample.RateErrorMs, 6);
        Assert.Equal(1UL, metrics.MissedBeats);
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
    public void SpuriousShortAEvents_DoNotAdvanceBeatCounterOrRateSchedule()
    {
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.0, 30.0, 95.0, 125.0);

        Assert.False(updates[2].BeatTimingSampleUpdated);
        Assert.Equal(3UL, updates[3].BeatTimingSample.BeatNumber);
        Assert.True(updates[3].BeatTimingSample.IsTic);
        Assert.Equal(0.0, updates[3].BeatTimingSample.RateErrorMs, 6);
        Assert.Equal(4UL, updates[4].BeatTimingSample.BeatNumber);
        Assert.Equal(0UL, metrics.MissedBeats);
    }

    [Fact]
    public void RlsRate_RestartsCleanAcrossADetectionGap()
    {
        // A 190 ms interval is a detection gap with a sub-beat residual: the
        // re-anchored schedule steps by 60 ms, which a regression window
        // mixing pre- and post-gap points would report as a slope spike of
        // thousands of s/d - permanently recorded by the cumulative rate
        // statistics. The estimators restart at the gap instead: the gap
        // sample reports rate-invalid, the reading returns within a few
        // beats, and no emitted sample ever carries the spike.
        WatchMetrics metrics = NewMetrics();
        double[] intervals = Enumerable.Repeat(125.0, 7)
            .Append(190.0)
            .Concat(Enumerable.Repeat(125.0, 8))
            .ToArray();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, intervals);

        Assert.False(updates[8].BeatTimingSample.RateValid);
        Assert.Contains(updates.Skip(9), u => u.BeatTimingSample.RateValid);
        Assert.All(
            updates.Where(u => u.BeatTimingSampleUpdated && u.BeatTimingSample.RateValid),
            u => Assert.InRange(u.BeatTimingSample.RateSPerDay, -1.0, 1.0));
    }

    [Fact]
    public void BeatTimingSample_CarriesBeatNumberPhaseAndRateError()
    {
        // Exact nominal intervals: the zero-offset anchor makes every Error Rate 0.
        WatchMetrics metrics = NewMetrics();
        List<WatchMetricsUpdate> updates = FeedAEvents(metrics, 125.0, 125.0);

        Assert.True(updates[0].BeatTimingSampleUpdated);
        Assert.Equal(1UL, updates[0].BeatTimingSample.BeatNumber);
        Assert.True(updates[0].BeatTimingSample.IsTic);
        Assert.Equal(0.0, updates[0].BeatTimingSample.TimeS, 9);
        Assert.Equal(28800, updates[0].BeatTimingSample.Bph);

        Assert.Equal(2UL, updates[1].BeatTimingSample.BeatNumber);
        Assert.False(updates[1].BeatTimingSample.IsTic);
        Assert.Equal(BeatSamples / SampleRate, updates[1].BeatTimingSample.TimeS, 9);
        Assert.Equal(0.0, updates[1].BeatTimingSample.RateErrorMs, 6);

        Assert.True(updates[2].BeatTimingSample.IsTic);
    }

    [Fact]
    public void BphReLock_RestartsTheMeasurementSegment()
    {
        // The detector drops the batch in which sync is lost, so a watch swap
        // surfaces here as a direct BPH change (18000 BPH, 200 ms beats ->
        // 36000 BPH, 100 ms beats). The segment must re-anchor: stale _bph
        // would mislabel every sample and gate signed measures with the old
        // 200 ms nominal period.
        WatchMetrics metrics = NewMetrics();
        double sample = 0.0;
        metrics.HandleAEvent(sample, true, 18000.0);
        foreach (double ms in new[] { 200.0, 200.0, 200.0 })
        {
            sample += ms / 1000.0 * SampleRate;
            metrics.HandleAEvent(sample, true, 18000.0);
        }

        var updates = new List<WatchMetricsUpdate>();
        foreach (double ms in new[] { 100.0, 100.8, 99.2, 100.8, 99.2 })
        {
            sample += ms / 1000.0 * SampleRate;
            updates.Add(metrics.HandleAEvent(sample, true, 36000.0));
        }

        // First post-re-lock event restarts the segment numbering.
        Assert.Equal(1UL, updates[0].BeatTimingSample.BeatNumber);
        Assert.True(updates[0].BeatTimingSample.IsTic);
        Assert.All(updates, u => Assert.Equal(36000, u.BeatTimingSample.Bph));

        // Signed measures come back on the new 100 ms nominal (t1=100.8, t2=99.2).
        Assert.True(updates[2].BeatTimingSample.BeatErrorValid);
        Assert.Equal(0.8, updates[2].BeatTimingSample.BeatErrorSignedMs, 6);
        Assert.True(updates[2].DerivedMeasures.DiffTicTacValid);
        Assert.Equal(1.6, updates[2].DerivedMeasures.DiffTicTacMs, 6);
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
        // BPH=3600: half-period 1 s. A->C interval of 1/6 s puts the sine argument at
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
        // A->C of a quarter period at BPH=3600 gives sin(pi/2)=1 -> 52 deg; shrink the
        // interval until the estimate exceeds 360 and the sample must be suppressed.
        const double bph = 3600.0;
        WatchMetrics metrics = NewMetrics();
        metrics.HandleAEvent(0.0, true, bph);

        // sin arg ~ 0.04 rad -> amplitude ~ 1300 deg (> 360): invalid.
        WatchMetricsUpdate update = metrics.HandleCEvent(600.0, true, bph);

        Assert.False(update.AmplitudeSampleUpdated);
    }

    [Fact]
    public void AmplitudeSample_NotEmittedWhenEstimateIsNegative()
    {
        // A C event past the half-cycle (t_AC > T/2) drives the escapement
        // equation's Sin() negative, so the computed amplitude is negative. A
        // mispaired/delayed C must be rejected, not shown as a real measurement.
        const double bph = 3600.0; // period 1 s, so t_AC > 0.5 s crosses the lobe
        WatchMetrics metrics = NewMetrics();
        metrics.HandleAEvent(0.0, true, bph);

        // t_AC = 1.1 s: sin(1.1*pi) < 0 -> amplitude = 52 / sin(...) < 0.
        WatchMetricsUpdate update = metrics.HandleCEvent(SampleRate * 1.1, true, bph);

        Assert.False(update.AmplitudeSampleUpdated);
    }
}
