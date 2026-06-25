using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Inference;

namespace TimeGrapher.Verify;

/// <summary>
/// Real-WAV A/B demo for the TinyML signal-quality gate: runs the same
/// recording through the detector with no gate and with the
/// <see cref="OnnxBeatEventGate"/>, and reports the bad-data veto rate.
///
/// The gate's job is "Bad Data Rejection / Signal Quality Classification", so
/// the meaningful measure is how many events it rejects: on a clean watch it
/// should veto almost none (it must not throw away good escapement beats), and
/// on a recording whose beat is followed by a recurring noise (mine_false) it
/// should veto the spurious interlopers. Detected BPH is reported for context
/// but is decided upstream of the gate, so it is identical in both arms - the
/// gate cleans the metric stream, it does not re-rate the watch.
/// </summary>
internal static class GateAbDemo
{
    private const int Block = 4096;

    public static void Run(TextWriter outw, string path)
    {
        string name = Path.GetFileName(path);
        Arm off = Analyze(path, gate: null);
        Arm onnx = Analyze(path, gate: OnnxBeatEventGate.LoadDefault());
        outw.WriteLine($"{name} A/B (off vs onnx gate):");
        outw.WriteLine(Format("off ", off));
        outw.WriteLine(Format("onnx", onnx));
    }

    private static string Format(string arm, Arm a)
    {
        int seen = a.KeptA + a.KeptC + (int)a.Vetoed;
        double rate = seen > 0 ? (double)a.Vetoed / seen : 0.0;
        return string.Format(CultureInfo.InvariantCulture,
            "  {0}: BPH={1,5} sync={2,-9} kept(A+C)={3,4} vetoed={4,4} ({5,5:P1} of {6} events)",
            arm, a.Bph, a.Sync, a.KeptA + a.KeptC, a.Vetoed, rate, seen);
    }

    private readonly record struct Arm(int Bph, TgSyncStatus Sync, int KeptA, int KeptC, ulong Vetoed);

    private static Arm Analyze(string path, IBeatEventGate? gate)
    {
        WavData wav = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);
        int fs = wav.SampleRate;
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0,
            EventGate: gate != null ? new BeatEventGateConfig(gate) : null));

        int keptA = 0, keptC = 0;
        DetectorResultSnapshot last = default!;

        void Consume(DetectorMetricsBlockUpdate u)
        {
            last = u.Result;
            foreach (DetectedEventUpdate ev in u.MetricsEvents)
            {
                if (ev.Event.Type == TgEventType.A) keptA++;
                else if (ev.Event.Type == TgEventType.C) keptC++;
            }
        }

        float[] s = wav.Samples;
        int off = 0;
        while (off < s.Length) { int n = Math.Min(Block, s.Length - off); Consume(engine.Process(new ReadOnlySpan<float>(s, off, n))); off += n; }
        Consume(engine.Flush());

        return new Arm(last.DetectedBph, last.SyncStatus, keptA, keptC, last.VetoedEvents);
    }
}
