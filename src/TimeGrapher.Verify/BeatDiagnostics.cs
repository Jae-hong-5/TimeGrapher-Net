using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Detection;

namespace TimeGrapher.Verify;

/// <summary>
/// Ground-truth-free B->A diagnostic over the metrics/display A/C stream. A
/// beat where the detector latches B as A shows two matching signatures: the A
/// phase residual (deviation from the locked, beat-error-corrected cadence) is a
/// positive outlier (A is ~2-4 ms late), and that beat's A->C interval (the
/// amplitude proxy) shortens. Running with a refiner arm
/// (<c>--landmark=...</c>) lets off-vs-refiner be compared by the same measure.
/// See docs/for-ai/LANDMARK_REFINER_BEAT_DIAGNOSIS.md.
/// </summary>
internal static class BeatDiagnostics
{
    private const int Block = 4096;
    private const double SettleS = 2.0;

    internal readonly record struct Result(
        int ACount, double ResidStdMs, double MaxLateMs, int Late1Ms, int Late2Ms,
        double AcMedianMs, int AcShort, int AcCount);

    public static void Run(TextWriter outw, string path, BeatLandmarkRefinerConfig? refiner, double rescueScale = 0.0)
    {
        WavData wav = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);
        int fs = wav.SampleRate;
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0, Refiner: refiner,
            PhaseGuideOnsetRescueScale: rescueScale));

        var aTimes = new List<double>();
        var beats = new List<(double A, double C)>();
        double? pendA = null;

        void Consume(DetectorMetricsBlockUpdate u)
        {
            foreach (DetectedEventUpdate ev in u.MetricsEvents)
            {
                double t = ev.EventSample / fs;
                if (ev.Event.Type == TgEventType.A) { pendA = t; aTimes.Add(t); }
                else if (ev.Event.Type == TgEventType.C && pendA != null) { beats.Add((pendA.Value, t)); pendA = null; }
            }
        }

        float[] s = wav.Samples;
        int off = 0;
        while (off < s.Length) { int n = Math.Min(Block, s.Length - off); Consume(engine.Process(new ReadOnlySpan<float>(s, off, n))); off += n; }
        Consume(engine.Flush());

        string arm = refiner?.Refiner.Name ?? (rescueScale > 0.0 ? $"rescue:{rescueScale:0.##}" : "off");
        string name = Path.GetFileName(path);
        Result r = Analyze(aTimes, beats, SettleS);
        if (r.ACount < 10)
        {
            outw.WriteLine($"{name} [{arm}]: too few A events ({r.ACount})");
            return;
        }
        outw.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0} [{1}]: A={2} resid_std={3:F3}ms max+={4:F2}ms lateA(>1ms)={5} (>2ms)={6} | A->C med={7:F2}ms short={8}/{9}",
            name, arm, r.ACount, r.ResidStdMs, r.MaxLateMs, r.Late1Ms, r.Late2Ms, r.AcMedianMs, r.AcShort, r.AcCount));
    }

    /// <summary>
    /// Pure stats over A times and A/C beats (post-settle): A phase residual in ms
    /// (rate fit + per-parity beat-error removal) and A->C dips. Exposed for tests.
    /// </summary>
    internal static Result Analyze(IReadOnlyList<double> aTimes, IReadOnlyList<(double A, double C)> beats, double settleS)
    {
        var a = aTimes.Where(t => t > settleS).ToList();
        int n = a.Count;
        if (n < 10)
        {
            return new Result(n, 0, 0, 0, 0, 0, 0, 0);
        }

        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++) { sx += i; sy += a[i]; sxx += (double)i * i; sxy += i * a[i]; }
        double slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
        double icpt = (sy - slope * sx) / n;

        var res = new double[n];
        double e0 = 0, e1 = 0; int c0 = 0, c1 = 0;
        for (int i = 0; i < n; i++) { res[i] = a[i] - (icpt + slope * i); if (i % 2 == 0) { e0 += res[i]; c0++; } else { e1 += res[i]; c1++; } }
        e0 /= c0 == 0 ? 1 : c0;
        e1 /= c1 == 0 ? 1 : c1;
        for (int i = 0; i < n; i++) res[i] = (res[i] - (i % 2 == 0 ? e0 : e1)) * 1000.0;

        double mean = res.Average();
        double std = Math.Sqrt(res.Select(x => (x - mean) * (x - mean)).Average());
        int late1 = res.Count(x => x > 1.0);
        int late2 = res.Count(x => x > 2.0);

        var ac = beats.Where(b => b.A > settleS).Select(b => (b.C - b.A) * 1000.0).OrderBy(x => x).ToList();
        double acMed = ac.Count > 0 ? ac[ac.Count / 2] : 0.0;
        int acShort = ac.Count(x => x < acMed - 1.5);

        return new Result(n, std, res.Max(), late1, late2, acMed, acShort, ac.Count);
    }
}
