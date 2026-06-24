using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Synthetic-truth tests proving the refiner pipeline moves the metrics C toward
/// the true landmark. A weak-C / strong-B packet (built with the per-cluster
/// amplitude knobs) drives the detector to latch the louder B lobe as C; an
/// oracle refiner that proposes the ground-truth C (captured from the synth's
/// event side channel) then collapses the C-timing error. This isolates the
/// pipeline's ability to carry a correct landmark into the metrics from whether
/// any particular model is accurate.
/// </summary>
public sealed class BeatLandmarkRefinerSyntheticTests
{
    private const int Fs = 48000;

    /// <summary>Proposes the ground-truth C nearest the detector candidate.</summary>
    private sealed class OracleCRefiner : IBeatLandmarkRefiner
    {
        private readonly double[] _trueCs;
        public OracleCRefiner(double[] trueCs) { _trueCs = (double[])trueCs.Clone(); Array.Sort(_trueCs); }

        public string Name => "oracle-c";
        public double WindowPreMs => 0.0;
        public double WindowPostMs => 0.0;

        public BeatLandmarkRefinement Refine(ReadOnlySpan<float> envelopeWindow, int aOffsetInWindow,
                                             int cOffsetInWindow, double sampleRate, in BeatLandmarkCandidate candidate)
            => new(Accepted: true, CorrectedC: true, CorrectedCSample: Nearest(_trueCs, candidate.CSample), CConfidence: 1.0f);

        public void Reset() { }
    }

    private static double Nearest(double[] sortedAscending, double value)
    {
        int idx = Array.BinarySearch(sortedAscending, value);
        if (idx >= 0) return sortedAscending[idx];
        idx = ~idx;
        if (idx == 0) return sortedAscending[0];
        if (idx == sortedAscending.Length) return sortedAscending[^1];
        double lo = sortedAscending[idx - 1];
        double hi = sortedAscending[idx];
        return (value - lo) <= (hi - value) ? lo : hi;
    }

    private static (float[] Pcm, double[] TrueCSamples) GenerateWeakC(double cScale, double bScale)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = 21600;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.0;
        cfg.EnableRealisticPacket = 1;
        cfg.CClusterLevelScale = cScale;
        cfg.BClusterLevelScale = bScale;

        var synth = new WatchSynthStream(cfg);
        const int seconds = 12;
        int n = Fs * seconds;
        var pcm = new float[n];
        var trueCs = new List<double>();
        var block = new float[4096];
        var events = new WatchSynthStreamEvent[64];
        int written = 0;
        while (written < n)
        {
            int slice = Math.Min(block.Length, n - written);
            WatchSynthStreamFillResult r = synth.FillF32(block.AsSpan(0, slice), events);
            Array.Copy(block, 0, pcm, written, slice);
            for (int i = 0; i < r.EventsWritten; i++)
            {
                // Ground-truth C = packet onset + A->C time.
                trueCs.Add((events[i].TimeS + events[i].AToCTimeS) * Fs);
            }
            written += slice;
        }
        return (pcm, trueCs.ToArray());
    }

    private static List<double> RunCSamples(IBeatLandmarkRefiner? refiner, float[] pcm)
    {
        var config = new DetectorMetricsEngineConfig(
            SampleRate: Fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0,
            // Generous clamp: this test isolates "does the pipeline carry the
            // truth into the metrics", not the safety clamp (covered elsewhere).
            Refiner: refiner != null ? new BeatLandmarkRefinerConfig(refiner, CClampPreMs: 20.0, CClampPostMs: 20.0) : null);
        var engine = new DetectorMetricsEngine(config);
        var cSamples = new List<double>();

        void Collect(DetectorMetricsBlockUpdate u)
        {
            foreach (DetectedEventUpdate ev in u.MetricsEvents)
            {
                if (ev.Event.Type == TgEventType.C)
                {
                    cSamples.Add(ev.EventSample);
                }
            }
        }

        int off = 0;
        while (off < pcm.Length)
        {
            int slice = Math.Min(4096, pcm.Length - off);
            Collect(engine.Process(pcm.AsSpan(off, slice)));
            off += slice;
        }
        Collect(engine.Flush());
        return cSamples;
    }

    private static double MedianAbsErrorMs(List<double> samples, double[] truth)
    {
        var errs = new List<double>();
        foreach (double s in samples)
        {
            // Settle: ignore the first 2 s while sync/metrics stabilise.
            if (s < 2.0 * Fs) continue;
            errs.Add(Math.Abs(s - Nearest(truth, s)) / Fs * 1000.0);
        }
        errs.Sort();
        return errs.Count == 0 ? 0.0 : errs[errs.Count / 2];
    }

    /// <summary>Proposes the ground-truth A nearest the detector candidate.</summary>
    private sealed class OracleARefiner : IBeatLandmarkRefiner
    {
        private readonly double[] _trueAs;
        public OracleARefiner(double[] trueAs) { _trueAs = (double[])trueAs.Clone(); Array.Sort(_trueAs); }

        public string Name => "oracle-a";
        public double WindowPreMs => 0.0;
        public double WindowPostMs => 0.0;

        public BeatLandmarkRefinement Refine(ReadOnlySpan<float> envelopeWindow, int aOffsetInWindow,
                                             int cOffsetInWindow, double sampleRate, in BeatLandmarkCandidate candidate)
            => new(Accepted: true, CorrectedC: false, CorrectedCSample: 0.0, CConfidence: 0.0f,
                   CorrectedA: true, CorrectedASample: Nearest(_trueAs, candidate.ASample), AConfidence: 1.0f);

        public void Reset() { }
    }

    private static (float[] Pcm, double[] TrueASamples) GenerateWeakA(double aScale, double bScale)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = 21600;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.0;
        cfg.EnableRealisticPacket = 1;
        cfg.AClusterLevelScale = aScale;
        cfg.BClusterLevelScale = bScale;

        var synth = new WatchSynthStream(cfg);
        const int seconds = 12;
        int n = Fs * seconds;
        var pcm = new float[n];
        var trueAs = new List<double>();
        var block = new float[4096];
        var events = new WatchSynthStreamEvent[64];
        int written = 0;
        while (written < n)
        {
            int slice = Math.Min(block.Length, n - written);
            WatchSynthStreamFillResult r = synth.FillF32(block.AsSpan(0, slice), events);
            Array.Copy(block, 0, pcm, written, slice);
            for (int i = 0; i < r.EventsWritten; i++)
            {
                trueAs.Add(events[i].TimeS * Fs); // ground-truth A = packet onset
            }
            written += slice;
        }
        return (pcm, trueAs.ToArray());
    }

    private static List<double> RunASamples(IBeatLandmarkRefiner? refiner, float[] pcm)
    {
        var config = new DetectorMetricsEngineConfig(
            SampleRate: Fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0,
            Refiner: refiner != null ? new BeatLandmarkRefinerConfig(refiner, AClampPreMs: 20.0, AClampPostMs: 20.0) : null);
        var engine = new DetectorMetricsEngine(config);
        var aSamples = new List<double>();

        void Collect(DetectorMetricsBlockUpdate u)
        {
            foreach (DetectedEventUpdate ev in u.MetricsEvents)
            {
                if (ev.Event.Type == TgEventType.A)
                {
                    aSamples.Add(ev.EventSample);
                }
            }
        }

        int off = 0;
        while (off < pcm.Length)
        {
            int slice = Math.Min(4096, pcm.Length - off);
            Collect(engine.Process(pcm.AsSpan(off, slice)));
            off += slice;
        }
        Collect(engine.Flush());
        return aSamples;
    }

    [Fact]
    public void OracleA_OnWeakAFixture_CollapsesAtimingErrorTowardTruth()
    {
        // Weak A (cluster scaled to 0.3; B and C left NORMAL): the weak A cluster
        // fails to cross the onset threshold, so the detector latches the louder
        // B lobe as A - a ~2 ms error on the timing reference. This is the more
        // serious failure: B->A corrupts the A timing that rate and beat error
        // are built on, not just amplitude. And unlike B->C it needs no extreme B
        // boost - weakening A alone breaks it (a strong A is timed to 0.13 ms),
        // so it is also the more realistic failure.
        (float[] pcm, double[] trueAs) = GenerateWeakA(aScale: 0.3, bScale: 1.0);

        double detectorErrMs = MedianAbsErrorMs(RunASamples(null, pcm), trueAs);
        double oracleErrMs = MedianAbsErrorMs(RunASamples(new OracleARefiner(trueAs), pcm), trueAs);

        Assert.True(detectorErrMs > 1.5,
            $"fixture is not adversarial: detector median A error {detectorErrMs:F3} ms");
        Assert.True(oracleErrMs < 0.5,
            $"corrected A should land near truth, got {oracleErrMs:F3} ms");
        Assert.True(oracleErrMs < detectorErrMs * 0.25,
            $"oracle A error {oracleErrMs:F3} ms should be far below detector {detectorErrMs:F3} ms");
    }

    [Fact]
    public void OracleC_OnWeakCStrongBFixture_CollapsesCtimingErrorTowardTruth()
    {
        // B->C is the LESS likely failure: the detector is robust to it and only
        // mis-times C when B overwhelmingly dominates C (a sweep shows the flip
        // needs C <= ~0.05 AND B >= ~10x; milder weak-C leaves C correctly timed).
        // This fixture is therefore a deliberate caricature whose job is to
        // exercise the C-correction path end to end - not a claim that B->C is
        // common. The realistic, more serious case is B->A above.
        (float[] pcm, double[] trueCs) = GenerateWeakC(cScale: 0.05, bScale: 10.0);

        double detectorErrMs = MedianAbsErrorMs(RunCSamples(null, pcm), trueCs);
        double oracleErrMs = MedianAbsErrorMs(RunCSamples(new OracleCRefiner(trueCs), pcm), trueCs);

        // Premise: in this caricature the detector mis-times C by ms.
        Assert.True(detectorErrMs > 2.0,
            $"fixture is not adversarial: detector median C error {detectorErrMs:F3} ms");
        // The pipeline carries the true C into the metrics: the corrected C lands
        // on the true landmark, collapsing the error.
        Assert.True(oracleErrMs < 1.0,
            $"corrected C should land near truth, got {oracleErrMs:F3} ms");
        Assert.True(oracleErrMs < detectorErrMs * 0.25,
            $"oracle C error {oracleErrMs:F3} ms should be far below detector {detectorErrMs:F3} ms");
    }
}
