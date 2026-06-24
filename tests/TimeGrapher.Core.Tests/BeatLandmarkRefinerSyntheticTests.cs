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

    private static double MedianCErrorMs(List<double> cSamples, double[] trueCs)
    {
        var errs = new List<double>();
        foreach (double c in cSamples)
        {
            // Settle: ignore the first 2 s while sync/metrics stabilise.
            if (c < 2.0 * Fs) continue;
            errs.Add(Math.Abs(c - Nearest(trueCs, c)) / Fs * 1000.0);
        }
        errs.Sort();
        return errs.Count == 0 ? 0.0 : errs[errs.Count / 2];
    }

    [Fact]
    public void OracleC_OnWeakCStrongBFixture_CollapsesCtimingErrorTowardTruth()
    {
        // Weak C (anchor scaled to 0.02) with a strong B cluster (10x): the
        // detector latches B as C. The flip is stable - neighbouring scales on
        // both sides give the same ~5 ms mis-timing, not a knife-edge.
        (float[] pcm, double[] trueCs) = GenerateWeakC(cScale: 0.02, bScale: 10.0);

        double detectorErrMs = MedianCErrorMs(RunCSamples(null, pcm), trueCs);
        double oracleErrMs = MedianCErrorMs(RunCSamples(new OracleCRefiner(trueCs), pcm), trueCs);

        // Premise: the fixture is adversarial - the detector mis-times C by ms.
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
