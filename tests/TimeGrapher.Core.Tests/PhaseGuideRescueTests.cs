using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The post-lock phase-guided onset rescue: on a weak-A packet the detector
/// latches B as A (a late timing-reference error); enabling the rescue lowers
/// the onset threshold inside the phase window so the weak A is caught at the
/// expected phase, cutting the A-timing error. Default (scale 0) is off and
/// covered as bit-identical by the golden-master tests.
/// </summary>
public sealed class PhaseGuideRescueTests
{
    private const int Fs = 48000;

    private static (float[] Pcm, double[] TrueAs) WeakAStream()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = 21600;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.002;
        cfg.EnableRealisticPacket = 1;
        cfg.AClusterLevelScale = 0.3; // weak A -> detector tends to latch B

        var synth = new WatchSynthStream(cfg);
        int n = Fs * 10;
        var pcm = new float[n];
        var trueAs = new List<double>();
        var block = new float[4096];
        var ev = new WatchSynthStreamEvent[64];
        int w = 0;
        while (w < n)
        {
            int sl = Math.Min(block.Length, n - w);
            WatchSynthStreamFillResult r = synth.FillF32(block.AsSpan(0, sl), ev);
            Array.Copy(block, 0, pcm, w, sl);
            for (int i = 0; i < r.EventsWritten; i++) trueAs.Add(ev[i].TimeS * Fs);
            w += sl;
        }
        return (pcm, trueAs.ToArray());
    }

    private static double Nearest(double[] s, double v)
    {
        int i = Array.BinarySearch(s, v);
        if (i >= 0) return s[i];
        i = ~i;
        if (i == 0) return s[0];
        if (i == s.Length) return s[^1];
        return (v - s[i - 1] <= s[i] - v) ? s[i - 1] : s[i];
    }

    private static double MedianAErrorMs(float[] pcm, double[] trueAs, double rescue)
    {
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: Fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0, PhaseGuideOnsetRescueScale: rescue));
        var aSamples = new List<double>();
        void Collect(DetectorMetricsBlockUpdate u)
        {
            foreach (DetectedEventUpdate e in u.MetricsEvents)
                if (e.Event.Type == TgEventType.A) aSamples.Add(e.EventSample);
        }
        int off = 0;
        while (off < pcm.Length) { int sl = Math.Min(4096, pcm.Length - off); Collect(engine.Process(pcm.AsSpan(off, sl))); off += sl; }
        Collect(engine.Flush());

        var truth = (double[])trueAs.Clone();
        Array.Sort(truth);
        var errs = new List<double>();
        foreach (double a in aSamples)
        {
            if (a < 2.0 * Fs) continue;
            errs.Add(Math.Abs(a - Nearest(truth, a)) / Fs * 1000.0);
        }
        errs.Sort();
        return errs.Count == 0 ? 0.0 : errs[errs.Count / 2];
    }

    [Fact]
    public void PhaseGuideRescue_ReducesWeakAtimingError()
    {
        (float[] pcm, double[] trueAs) = WeakAStream();
        double off = MedianAErrorMs(pcm, trueAs, rescue: 0.0);
        double rescue = MedianAErrorMs(pcm, trueAs, rescue: 0.4);

        Assert.True(off > 1.0, $"weak-A fixture should mis-time A without rescue, got {off:F2} ms");
        Assert.True(rescue < off * 0.6, $"rescue should cut the weak-A timing error: {rescue:F2} vs {off:F2} ms");
    }
}
