using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// End-to-end behavioral scenarios over the detector + metrics pipeline driven by the
/// deterministic synthetic watch stream: Error Rate direction, beat-error reflection,
/// manual-BPH mismatch, and the time-based sync-loss watchdog.
/// </summary>
public sealed class DetectorScenarioTests
{
    private static DetectorMetricsEngine NewEngine(int sampleRate, bool autoBph, int manualBph) =>
        new(new DetectorMetricsEngineConfig(
            SampleRate: sampleRate,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: autoBph,
            ManualBph: manualBph,
            HpfCutoffHz: 0.0));

    private static WatchSynthStreamConfig Synth(int sampleRate, int bph, double rateSPerDay, double beatErrorMs)
    {
        WatchSynthStreamConfig c = WatchSynthStreamConfig.Clean();
        c.SampleRateHz = (uint)sampleRate;
        c.Bph = bph;
        c.RateErrorSPerDay = rateSPerDay;
        c.BeatErrorMs = beatErrorMs;
        c.PcmPeakSignalLevel = 0.40;
        c.NoisePeakSignalLevel = 0.0;
        return c;
    }

    private static string FeedSeconds(
        DetectorMetricsEngine engine, WatchSynthStream synth, int sampleRate, int seconds,
        out DetectorMetricsBlockUpdate lastUpdate)
    {
        var block = new float[4096];
        string results = "";
        lastUpdate = engine.Flush();
        int remaining = sampleRate * seconds;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            lastUpdate = engine.Process(span);
            CaptureResults(lastUpdate, ref results);
            remaining -= slice;
        }

        lastUpdate = engine.Flush();
        CaptureResults(lastUpdate, ref results);
        return results;
    }

    private static DetectorMetricsBlockUpdate FeedSilence(DetectorMetricsEngine engine, int sampleRate, int seconds)
    {
        var block = new float[4096];
        DetectorMetricsBlockUpdate update = engine.Flush();
        int remaining = sampleRate * seconds;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Array.Clear(block, 0, slice);
            update = engine.Process(block.AsSpan(0, slice));
            remaining -= slice;
        }

        return update;
    }

    private static void CaptureResults(DetectorMetricsBlockUpdate update, ref string results)
    {
        foreach (DetectedEventUpdate ev in update.MetricsEvents)
        {
            if (ev.MetricsUpdate.ResultsUpdated)
            {
                results = ev.MetricsUpdate.ResultsText;
            }
        }
    }

    /// <summary>Extracts the numeric value wrapped in markers after a readout label.</summary>
    private static double Field(string results, string label)
    {
        int li = results.IndexOf(label, StringComparison.Ordinal);
        Assert.True(li >= 0, $"label '{label}' not found in '{results}'");
        int open = results.IndexOf(WatchMetrics.ValueSpanStart, li);
        int close = open >= 0 ? results.IndexOf(WatchMetrics.ValueSpanEnd, open) : -1;
        Assert.True(open >= 0 && close > open, $"no numeric value after '{label}' in '{results}'");
        string token = results.Substring(open + 1, close - open - 1).Trim();
        return double.Parse(token, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void RateError_FastAndSlowWatchesReadOppositeSigns()
    {
        const int sr = 48000, bph = 21600;
        string fast = FeedSeconds(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, +30.0, 0.0)), sr, 12, out DetectorMetricsBlockUpdate uFast);
        string slow = FeedSeconds(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, -30.0, 0.0)), sr, 12, out DetectorMetricsBlockUpdate uSlow);
        string clean = FeedSeconds(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, 0.0, 0.0)), sr, 12, out _);

        Assert.Equal(TgSyncStatus.Synced, uFast.Result.SyncStatus);
        Assert.Equal(TgSyncStatus.Synced, uSlow.Result.SyncStatus);

        double rFast = Field(fast, "Error Rate ");
        double rSlow = Field(slow, "Error Rate ");
        double rClean = Field(clean, "Error Rate ");

        Assert.True(rFast * rSlow < 0.0, $"fast={rFast} slow={rSlow} should differ in sign");
        Assert.True(Math.Abs(rFast) > 5.0 && Math.Abs(rSlow) > 5.0, $"fast={rFast} slow={rSlow}");
        Assert.True(Math.Abs(rClean) < 5.0, $"clean rate {rClean} should be near zero");
    }

    [Fact]
    public void BeatError_InjectedErrorShowsLargerThanCleanStream()
    {
        const int sr = 48000, bph = 21600;
        string withError = FeedSeconds(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, 0.0, 5.0)), sr, 12, out _);
        string clean = FeedSeconds(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, 0.0, 0.0)), sr, 12, out _);

        double beWith = Math.Abs(Field(withError, "BEAT ERROR "));
        double beClean = Math.Abs(Field(clean, "BEAT ERROR "));

        Assert.True(beWith > beClean + 1.0, $"injected beat error {beWith} vs clean {beClean}");
    }

    [Fact]
    public void ManualBph_WrongValueNeverSyncs()
    {
        const int sr = 48000;
        FeedSeconds(NewEngine(sr, autoBph: false, manualBph: 18000), new WatchSynthStream(Synth(sr, 21600, 0.0, 0.0)), sr, 8, out DetectorMetricsBlockUpdate update);

        // Manual BPH that does not match the signal must not lock; once enough events
        // are seen the detector reports the distinct Mismatch state (not a quiet
        // NotSynced fallback and never a false Synced).
        Assert.Equal(TgSyncStatus.Mismatch, update.Result.SyncStatus);
    }

    private static (DerivedTimingMeasures Derived, BeatTimingSample LastBeat) FeedCapturingDerived(
        DetectorMetricsEngine engine, WatchSynthStream synth, int sampleRate, int seconds)
    {
        var block = new float[4096];
        DerivedTimingMeasures derived = default;
        BeatTimingSample lastBeat = default;
        int remaining = sampleRate * seconds;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            DetectorMetricsBlockUpdate update = engine.Process(span);
            foreach (DetectedEventUpdate ev in update.MetricsEvents)
            {
                if (ev.MetricsUpdate.DerivedMeasuresUpdated)
                {
                    derived = ev.MetricsUpdate.DerivedMeasures;
                }
                if (ev.MetricsUpdate.BeatTimingSampleUpdated)
                {
                    lastBeat = ev.MetricsUpdate.BeatTimingSample;
                }
            }
            remaining -= slice;
        }

        return (derived, lastBeat);
    }

    [Fact]
    public void DerivedMeasures_DiffTicTacTracksInjectedBeatError()
    {
        // The synth applies +offset to one phase and -offset to the other, so a
        // 5 ms injected beat error makes tick and tock durations differ by ~10 ms.
        const int sr = 48000, bph = 21600;
        (DerivedTimingMeasures withError, BeatTimingSample beat) =
            FeedCapturingDerived(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, 0.0, 5.0)), sr, 12);
        (DerivedTimingMeasures clean, _) =
            FeedCapturingDerived(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, 0.0, 0.0)), sr, 12);

        Assert.True(withError.DiffTicTacValid);
        Assert.InRange(Math.Abs(withError.DiffTicTacMs), 8.0, 12.0);
        Assert.True(Math.Abs(clean.DiffTicTacMs) < 1.0, $"clean DiffTicTac {clean.DiffTicTacMs} should be near zero");

        // Signed beat error is half the tick-tock difference and keeps its sign.
        Assert.True(beat.BeatErrorValid);
        Assert.Equal(withError.DiffTicTacMs / 2.0, beat.BeatErrorSignedMs, 6);
    }

    [Fact]
    public void DerivedMeasures_AvgPeriodTracksSlowWatch()
    {
        // -30 s/d at 21600 BPH stretches each 166.67 ms beat by ~0.058 ms; the
        // measured-vs-expected averages must read positive (and near zero when clean).
        const int sr = 48000, bph = 21600;
        (DerivedTimingMeasures slow, _) =
            FeedCapturingDerived(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, -30.0, 0.0)), sr, 12);
        (DerivedTimingMeasures clean, _) =
            FeedCapturingDerived(NewEngine(sr, true, 0), new WatchSynthStream(Synth(sr, bph, 0.0, 0.0)), sr, 12);

        Assert.True(slow.AvgPeriodValid);
        Assert.True(slow.DiffPeriodValid);
        Assert.InRange(slow.AvgPeriodMs, 0.02, 0.12);
        Assert.InRange(slow.DiffPeriodMs, 0.02, 0.12);
        Assert.True(Math.Abs(clean.AvgPeriodMs) < 0.02, $"clean AvgPeriod {clean.AvgPeriodMs} should be near zero");
    }

    [Fact]
    public void SyncLoss_StatusDropsAfterSilence()
    {
        const int sr = 48000, bph = 21600;
        var engine = NewEngine(sr, true, 0);
        var synth = new WatchSynthStream(Synth(sr, bph, 0.0, 0.0));

        FeedSeconds(engine, synth, sr, 12, out DetectorMetricsBlockUpdate synced);
        Assert.Equal(TgSyncStatus.Synced, synced.Result.SyncStatus);

        // Feed silence block-by-block: the watchdog must raise the one-shot
        // SyncLostEvent exactly once and reset the locked BPH/period on that block,
        // then stay lost without re-raising the edge.
        var block = new float[4096];
        int remaining = sr * 6;
        int syncLostEvents = 0;
        DetectorMetricsBlockUpdate last = synced;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Array.Clear(block, 0, slice);
            last = engine.Process(block.AsSpan(0, slice));
            if (last.Result.SyncLostEvent)
            {
                syncLostEvents++;
                Assert.Equal(0, last.Result.DetectedBph);
                Assert.Equal(0.0, last.Result.MeasuredPeriodS);
            }
            remaining -= slice;
        }

        Assert.Equal(1, syncLostEvents);
        Assert.NotEqual(TgSyncStatus.Synced, last.Result.SyncStatus);
    }
}
