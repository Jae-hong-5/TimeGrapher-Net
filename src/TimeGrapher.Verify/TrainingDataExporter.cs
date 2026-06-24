using System.Globalization;
using System.Text;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Sim;

namespace TimeGrapher.Verify;

/// <summary>
/// Exports synthetic refiner-training rows to CSV. Each row is one beat: the
/// detector's delayed-envelope (ProcessedPcm) window - the same signal the
/// refiner sees at inference - plus the detector's A/C peak offsets and the
/// ground-truth A/C offsets captured from the synth's event side channel.
///
/// The scenario mix is deliberately weighted toward weak-A: B->A is the more
/// serious and more realistic failure (see TINYML_LANDMARK_REFINER_PLAN.md),
/// so weak-A cases out-number the rest. The CSV format is provisional - it
/// exists to seed an external trainer; the columns are stable enough to consume
/// without a schema file.
/// </summary>
internal static class TrainingDataExporter
{
    private const int Fs = 48000;
    // The window is anchored on the DETECTOR A. On a B->A mis-pick the detector A
    // sits at B, ~2-3 ms after the true onset, so Pre must reach back far enough
    // to still contain the true A; Post must reach the C across the BPH range.
    private const double PreMs = 6.0;
    private const double PostMs = 14.0;

    private readonly record struct Scenario(
        string Label, double AScale, double BScale, double CScale, double Noise, int Bph, ulong Seed);

    // The mix mirrors the real-sample diagnostic: B->A is the dominant failure and
    // its frequency rises as the pickup signal weakens (rare on a clean NH35,
    // ~1.5% on mine.wav, pervasive on the weak-signal mine_adapter.wav). So the
    // weak-A family dominates and spans A-weakness x noise; the rest is a small
    // contrast set (B->C is a rare caricature, kept minimal).
    private static Scenario[] BuildScenarios() =>
    [
        // weak-A: A too weak to cross the onset threshold, so B is latched as A.
        new("weak-a", 0.40, 1.0, 1.0, 0.000, 21600, 0x1001), // mild (intermittent late-A)
        new("weak-a", 0.30, 1.0, 1.0, 0.000, 18000, 0x1002),
        new("weak-a", 0.30, 1.0, 1.0, 0.000, 28800, 0x1003),
        new("weak-a", 0.20, 1.0, 1.0, 0.000, 21600, 0x1004), // strongly weak A
        // weak-A + noise: the weak-signal regime (mine_adapter-like, pervasive late-A).
        new("weak-a-noisy", 0.30, 1.0, 1.0, 0.006, 21600, 0x1005),
        new("weak-a-noisy", 0.25, 1.0, 1.0, 0.012, 21600, 0x1006),
        new("weak-a-noisy", 0.20, 1.0, 1.0, 0.012, 18000, 0x1007),
        // weak-A + elevated B (B more likely to win the onset threshold).
        new("weak-a-strongb", 0.30, 3.0, 1.0, 0.000, 21600, 0x1008),
        new("weak-a-strongb", 0.25, 3.0, 1.0, 0.006, 28800, 0x1009),
        // contrast (minority): the detector handles these well.
        new("clean", 1.0, 1.0, 1.0, 0.000, 21600, 0x2001),
        new("weak-c", 1.0, 1.0, 0.30, 0.000, 21600, 0x2002),
        new("b-gt-c", 1.0, 10.0, 0.05, 0.000, 21600, 0x2003), // B->C caricature (rare in reality)
        new("noisy", 1.0, 1.0, 1.0, 0.012, 21600, 0x2004),
    ];

    public static int Export(string outDir)
    {
        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, "landmark_training.csv");
        int pre = (int)(PreMs * 1e-3 * Fs);
        int post = (int)(PostMs * 1e-3 * Fs);
        int windowLen = pre + post;

        using var writer = new StreamWriter(path);
        WriteHeader(writer, windowLen);

        var perLabel = new Dictionary<string, int>(StringComparer.Ordinal);
        int total = 0;
        foreach (Scenario sc in BuildScenarios())
        {
            int rows = ExportScenario(writer, sc, pre, windowLen);
            perLabel[sc.Label] = perLabel.GetValueOrDefault(sc.Label) + rows;
            total += rows;
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "training export: {0} rows -> {1}", total, path));
        foreach (KeyValuePair<string, int> kv in perLabel)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1}", kv.Key, kv.Value));
        }
        return 0;
    }

    private static void WriteHeader(StreamWriter writer, int windowLen)
    {
        var sb = new StringBuilder();
        sb.Append("scenario,bph,a_scale,b_scale,c_scale,noise,sample_rate,window_len,pre_samples,");
        sb.Append("det_a_off,det_c_off,true_a_off,true_c_off,b_risk_a,b_risk_c");
        for (int i = 0; i < windowLen; i++)
        {
            sb.Append(",env_").Append(i.ToString(CultureInfo.InvariantCulture));
        }
        writer.WriteLine(sb.ToString());
    }

    private static int ExportScenario(StreamWriter writer, Scenario sc, int pre, int windowLen)
    {
        (float[] pcm, double[] trueAs, double[] trueCs) = Generate(sc);
        (List<float> env, ulong envBase, List<(TgEventType Type, double Sample, ulong Index)> events) = RunDetector(pcm);

        double bRiskA = sc.BScale / sc.AScale;
        double bRiskC = sc.BScale / sc.CScale;
        var row = new StringBuilder();
        int rows = 0;

        for (int i = 0; i + 1 < events.Count; i++)
        {
            if (events[i].Type != TgEventType.A || events[i + 1].Type != TgEventType.C)
            {
                continue;
            }

            double aSample = events[i].Sample;
            double cSample = events[i + 1].Sample;
            if (aSample < 2.0 * Fs) continue; // settle

            long winStartAbs = (long)events[i].Index - pre;
            long envStart = winStartAbs - (long)envBase;
            if (envStart < 0 || envStart + windowLen > env.Count) continue;

            double detAOff = aSample - winStartAbs;
            double detCOff = cSample - winStartAbs;
            double trueAOff = Nearest(trueAs, aSample) - winStartAbs;
            double trueCOff = Nearest(trueCs, cSample) - winStartAbs;
            // Keep only beats whose A and C both fall inside the window.
            if (detCOff >= windowLen || trueCOff < 0 || trueCOff >= windowLen || trueAOff < 0) continue;

            row.Clear();
            row.Append(sc.Label).Append(',')
               .Append(sc.Bph.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(F(sc.AScale)).Append(',').Append(F(sc.BScale)).Append(',').Append(F(sc.CScale)).Append(',')
               .Append(F(sc.Noise)).Append(',')
               .Append(Fs.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(windowLen.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(pre.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(F(detAOff)).Append(',').Append(F(detCOff)).Append(',')
               .Append(F(trueAOff)).Append(',').Append(F(trueCOff)).Append(',')
               .Append(F(bRiskA)).Append(',').Append(F(bRiskC));
            for (int k = 0; k < windowLen; k++)
            {
                row.Append(',').Append(F(env[(int)envStart + k]));
            }
            writer.WriteLine(row.ToString());
            rows++;
        }
        return rows;
    }

    private static (float[] Pcm, double[] TrueAs, double[] TrueCs) Generate(Scenario sc)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = sc.Bph;
        cfg.Seed = sc.Seed;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = sc.Noise;
        cfg.EnableRealisticPacket = 1;
        cfg.AClusterLevelScale = sc.AScale;
        cfg.BClusterLevelScale = sc.BScale;
        cfg.CClusterLevelScale = sc.CScale;

        var synth = new WatchSynthStream(cfg);
        const int seconds = 8;
        int n = Fs * seconds;
        var pcm = new float[n];
        var trueAs = new List<double>();
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
                trueAs.Add(events[i].TimeS * Fs);
                trueCs.Add((events[i].TimeS + events[i].AToCTimeS) * Fs);
            }
            written += slice;
        }
        return (pcm, trueAs.ToArray(), trueCs.ToArray());
    }

    private static (List<float> Env, ulong EnvBase, List<(TgEventType, double, ulong)> Events) RunDetector(float[] pcm)
    {
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: Fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0));
        var env = new List<float>();
        ulong envBase = 0;
        bool haveBase = false;
        var events = new List<(TgEventType, double, ulong)>();

        void Consume(DetectorMetricsBlockUpdate u)
        {
            DetectorResultSnapshot r = u.Result;
            if (r.ProcessedPcmLen > 0)
            {
                if (!haveBase) { envBase = r.ProcessedPcmStartSample; haveBase = true; }
                ulong expected = envBase + (ulong)env.Count;
                ReadOnlySpan<float> span = r.ProcessedPcm.Span;
                // Pad any gap, skip any overlap, so env[k] maps to absolute sample envBase + k.
                for (ulong g = expected; g < r.ProcessedPcmStartSample; g++) env.Add(0f);
                int skip = (int)Math.Max(0L, (long)expected - (long)r.ProcessedPcmStartSample);
                for (int i = skip; i < r.ProcessedPcmLen; i++) env.Add(span[i]);
            }
            foreach (TgEvent ev in r.Events)
            {
                events.Add((ev.Type, ev.SampleIndex + ev.SubSampleOffset, ev.SampleIndex));
            }
        }

        int off = 0;
        while (off < pcm.Length)
        {
            int slice = Math.Min(4096, pcm.Length - off);
            Consume(engine.Process(pcm.AsSpan(off, slice)));
            off += slice;
        }
        Consume(engine.Flush());
        return (env, envBase, events);
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

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
