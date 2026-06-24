using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Contract tests for the engine-level landmark-refiner host using a scriptable
/// refiner double: a fall-back (no-op) refiner routes exactly what the
/// un-refined engine routes, an accepted C correction moves the metrics/display
/// C but never the raw snapshot (and never breaks lock), corrections are clamped
/// to the configured window, low-confidence proposals fall back, windowed
/// refiners receive A/C-aligned windows via delayed release, Flush
/// force-releases the tail, and sync loss resets the refiner.
/// </summary>
public sealed class BeatLandmarkRefinerTests
{
    private sealed class ScriptedRefiner : IBeatLandmarkRefiner
    {
        public double PreMs;
        public double PostMs;
        public int ResetCount;
        public Func<BeatLandmarkCandidate, BeatLandmarkRefinement> RefineFunc =
            _ => BeatLandmarkRefinement.Fallback;
        public readonly List<(int Length, int AOffset, int COffset)> Windows = new();

        public string Name => "scripted";
        public double WindowPreMs => PreMs;
        public double WindowPostMs => PostMs;

        public BeatLandmarkRefinement Refine(ReadOnlySpan<float> envelopeWindow, int aOffsetInWindow,
                                             int cOffsetInWindow, double sampleRate, in BeatLandmarkCandidate candidate)
        {
            Windows.Add((envelopeWindow.Length, aOffsetInWindow, cOffsetInWindow));
            return RefineFunc(candidate);
        }

        public void Reset() => ResetCount++;
    }

    private static DetectorMetricsEngine NewEngine(IBeatLandmarkRefiner? refiner) =>
        new(new DetectorMetricsEngineConfig(
            SampleRate: 48000,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0,
            Refiner: refiner != null ? new BeatLandmarkRefinerConfig(refiner) : null));

    private static WatchSynthStreamConfig CleanStream()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = 48000;
        cfg.Bph = 21600;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.0;
        return cfg;
    }

    private sealed record RunResult(
        List<(TgEventType Type, ulong SampleIndex)> MetricsEvents,
        List<(TgEventType Type, ulong SampleIndex)> DisplayEvents,
        List<(TgEventType Type, ulong SampleIndex)> SnapshotEvents,
        DetectorResultSnapshot FinalSnapshot);

    private static RunResult Run(DetectorMetricsEngine engine, int seconds, int silenceTailSeconds = 0)
    {
        var synth = new WatchSynthStream(CleanStream());
        var metrics = new List<(TgEventType, ulong)>();
        var display = new List<(TgEventType, ulong)>();
        var snapshot = new List<(TgEventType, ulong)>();
        var block = new float[4096];
        DetectorMetricsBlockUpdate update = default!;

        void Capture(DetectorMetricsBlockUpdate u)
        {
            foreach (DetectedEventUpdate ev in u.MetricsEvents) metrics.Add((ev.Event.Type, ev.Event.SampleIndex));
            foreach (DetectedEventUpdate ev in u.DisplayEvents) display.Add((ev.Event.Type, ev.Event.SampleIndex));
            foreach (TgEvent ev in u.Result.Events) snapshot.Add((ev.Type, ev.SampleIndex));
        }

        int remaining = 48000 * seconds;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            synth.Generate(block.AsSpan(0, slice));
            update = engine.Process(block.AsSpan(0, slice));
            Capture(update);
            remaining -= slice;
        }
        remaining = 48000 * silenceTailSeconds;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Array.Clear(block, 0, slice);
            update = engine.Process(block.AsSpan(0, slice));
            Capture(update);
            remaining -= slice;
        }
        update = engine.Flush();
        Capture(update);
        return new RunResult(metrics, display, snapshot, update.Result);
    }

    [Fact]
    public void NoOpRefiner_RoutesExactlyWhatTheUnrefinedEngineRoutes()
    {
        RunResult unrefined = Run(NewEngine(null), 8);
        RunResult noop = Run(NewEngine(new NoOpBeatLandmarkRefiner()), 8);

        Assert.Equal(unrefined.MetricsEvents, noop.MetricsEvents);
        Assert.Equal(unrefined.DisplayEvents, noop.DisplayEvents);
        Assert.Equal(unrefined.SnapshotEvents, noop.SnapshotEvents);
    }

    [Fact]
    public void AcceptedCCorrection_MovesMetricsAndDisplay_NotTheRawSnapshot_AndClamps()
    {
        // Shove every C 1000 samples later; the C clamp is +6 ms = 288 samples at
        // 48 kHz, so the metrics/display C must move by exactly the clamp while
        // As and the raw snapshot are untouched.
        const int clampPost = (int)(0.006 * 48000);
        var refiner = new ScriptedRefiner
        {
            RefineFunc = c => new BeatLandmarkRefinement(
                Accepted: true, CorrectedC: true, CorrectedCSample: c.CSample + 1000.0, CConfidence: 1.0f),
        };
        RunResult unrefined = Run(NewEngine(null), 8);
        RunResult refined = Run(NewEngine(refiner), 8);

        Assert.Equal(unrefined.MetricsEvents.Count, refined.MetricsEvents.Count);
        // The raw snapshot stream is never altered by the refiner.
        Assert.Equal(unrefined.SnapshotEvents, refined.SnapshotEvents);
        // Lock is unaffected (refiner sits after BPH/PLL).
        Assert.Equal(TgSyncStatus.Synced, refined.FinalSnapshot.SyncStatus);

        bool sawC = false;
        for (int i = 0; i < unrefined.MetricsEvents.Count; i++)
        {
            (TgEventType type, ulong baseIdx) = unrefined.MetricsEvents[i];
            (TgEventType refType, ulong refIdx) = refined.MetricsEvents[i];
            Assert.Equal(type, refType);
            if (type == TgEventType.C)
            {
                sawC = true;
                Assert.Equal(baseIdx + clampPost, refIdx);
            }
            else
            {
                Assert.Equal(baseIdx, refIdx);
            }
        }
        Assert.True(sawC, "expected at least one C event");
        Assert.Equal(refined.MetricsEvents, refined.DisplayEvents);
    }

    [Fact]
    public void LowConfidenceCorrection_FallsBackToDetectorValue()
    {
        var refiner = new ScriptedRefiner
        {
            // Below the default 0.5 confidence floor: the host must ignore it.
            RefineFunc = c => new BeatLandmarkRefinement(
                Accepted: true, CorrectedC: true, CorrectedCSample: c.CSample + 100.0, CConfidence: 0.1f),
        };
        RunResult unrefined = Run(NewEngine(null), 8);
        RunResult refined = Run(NewEngine(refiner), 8);

        Assert.Equal(unrefined.MetricsEvents, refined.MetricsEvents);
    }

    [Fact]
    public void DeclinedCorrection_FallsBackToDetectorValue()
    {
        var refiner = new ScriptedRefiner
        {
            // Accepted = false (the fail-open default) even with a populated sample.
            RefineFunc = c => new BeatLandmarkRefinement(
                Accepted: false, CorrectedC: true, CorrectedCSample: c.CSample + 100.0, CConfidence: 1.0f),
        };
        RunResult unrefined = Run(NewEngine(null), 8);
        RunResult refined = Run(NewEngine(refiner), 8);

        Assert.Equal(unrefined.MetricsEvents, refined.MetricsEvents);
    }

    [Fact]
    public void WindowedRefiner_ReceivesWindowsWithAAndCAligned()
    {
        var refiner = new ScriptedRefiner { PreMs = 5.0, PostMs = 5.0 };
        Run(NewEngine(refiner), 8);

        var paired = refiner.Windows.Where(w => w.AOffset >= 0 && w.COffset >= 0).ToList();
        Assert.NotEmpty(paired);
        foreach ((int length, int aOffset, int cOffset) in paired)
        {
            // C comes after A within the same window, and both are inside it.
            Assert.True(cOffset > aOffset, $"C offset {cOffset} should follow A offset {aOffset}");
            Assert.True(cOffset < length, $"C offset {cOffset} should be inside the window of length {length}");
        }
    }

    [Fact]
    public void Flush_DeliversTheTailNothingStaysPending()
    {
        List<(TgEventType, ulong)> RunCut(IBeatLandmarkRefiner? refiner)
        {
            var engine = NewEngine(refiner);
            var synth = new WatchSynthStream(CleanStream());
            var routed = new List<(TgEventType, ulong)>();
            var block = new float[4096];
            int remaining = (int)(48000 * 2.40);
            while (remaining > 0)
            {
                int slice = Math.Min(block.Length, remaining);
                synth.Generate(block.AsSpan(0, slice));
                DetectorMetricsBlockUpdate u = engine.Process(block.AsSpan(0, slice));
                foreach (DetectedEventUpdate ev in u.MetricsEvents) routed.Add((ev.Event.Type, ev.Event.SampleIndex));
                remaining -= slice;
            }
            DetectorMetricsBlockUpdate fu = engine.Flush();
            foreach (DetectedEventUpdate ev in fu.MetricsEvents) routed.Add((ev.Event.Type, ev.Event.SampleIndex));
            return routed;
        }

        List<(TgEventType, ulong)> unrefined = RunCut(null);
        List<(TgEventType, ulong)> windowed = RunCut(new ScriptedRefiner { PreMs = 5.0, PostMs = 5.0 });

        Assert.NotEmpty(unrefined);
        Assert.Equal(unrefined, windowed);
    }

    [Fact]
    public void SyncLoss_ResetsTheRefiner()
    {
        var refiner = new ScriptedRefiner();
        RunResult result = Run(NewEngine(refiner), 6, silenceTailSeconds: 5);

        Assert.True(refiner.ResetCount >= 1,
            $"refiner.Reset() was called {refiner.ResetCount} times; expected >= 1 after sync loss");
        Assert.NotEqual(TgSyncStatus.Synced, result.FinalSnapshot.SyncStatus);
    }
}
