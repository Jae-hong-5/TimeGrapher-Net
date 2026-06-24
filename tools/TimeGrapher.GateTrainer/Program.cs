using Microsoft.ML;
using Microsoft.ML.Data;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;

// Signal-quality gate trainer. Extracts per-event 128-point envelope features
// (BeatWindowFeatures - the same contract OnnxBeatEventGate uses at inference)
// from the sample recordings, labels them, trains an ML.NET SDCA logistic
// regression "good escapement vs bad data" classifier, and exports the model
// the gate loads. Run from the repo root:
//   dotnet run --project tools/TimeGrapher.GateTrainer -c Release
//
// Labels (iteration 1):
//   - positive: every settled event from the clean watch recordings.
//   - negative: mine_false's off-cadence interlopers ("tock then noise"),
//     pseudo-labeled by a greedy cadence grid at the watch's true rate; the
//     on-cadence mine_false beats are kept as positives.

const double SettleS = 2.0;
const int Block = 4096;
const double WinPreMs = 2.0, WinPostMs = 6.0;   // MUST match OnnxBeatEventGate
const double FalseTrueBph = 21600.0;            // mine_false's true rate
const string SampleDir = "sample";
const string OutOnnx = "src/TimeGrapher.Inference/Models/tick-quality.onnx";

List<Ev> Extract(string path)
{
    WavData wav = WavFileReader.ReadMonoFloat(path, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);
    int fs = wav.SampleRate;
    var gate = new RecordingGate(WinPreMs, WinPostMs);
    var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
        SampleRate: fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
        AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0,
        EventGate: new BeatEventGateConfig(gate)));
    float[] s = wav.Samples;
    int off = 0;
    while (off < s.Length) { int n = Math.Min(Block, s.Length - off); engine.Process(new ReadOnlySpan<float>(s, off, n)); off += n; }
    engine.Flush();
    return gate.Events.Where(e => e.T > SettleS && e.Feat != null).ToList();
}

// Positives: clean watch recordings (BPH-named + the good "mine" captures).
var positiveFiles = Directory.GetFiles(SampleDir, "*BPH*.wav").ToList();
foreach (var extra in new[] { "mine.wav", "mine_usb.wav", "mine_adapter.wav" })
    positiveFiles.Add(Path.Combine(SampleDir, extra));

var pos = new List<float[]>();
foreach (var f in positiveFiles)
    foreach (var e in Extract(f))
        pos.Add(e.Feat!);

// mine_false: greedy cadence grid at the true period -> off-grid A = interloper.
var mf = Extract(Path.Combine(SampleDir, "mine_false.wav"));
double T = 3600.0 / FalseTrueBph;
const double Tol = 0.040;
var aEvents = mf.Where(e => e.Type == TgEventType.A).OrderBy(e => e.T).ToList();
var realA = new List<double>();
var mfPos = new List<float[]>();
var mfNeg = new List<float[]>();
if (aEvents.Count > 0)
{
    double expected = aEvents[0].T + T;
    realA.Add(aEvents[0].T); mfPos.Add(aEvents[0].Feat!);
    for (int i = 1; i < aEvents.Count; i++)
    {
        double t = aEvents[i].T;
        while (t > expected + Tol) expected += T;            // skip missed beats
        if (Math.Abs(t - expected) <= Tol)                   // on grid -> real beat
        {
            realA.Add(t); mfPos.Add(aEvents[i].Feat!); expected = t + T;
        }
        else                                                 // early -> interloper
        {
            mfNeg.Add(aEvents[i].Feat!);
        }
    }
}
// C events: good if they follow a real A at a normal lift (8-18 ms), else bad.
foreach (var c in mf.Where(e => e.Type == TgEventType.C))
{
    bool paired = realA.Any(a => c.T - a >= 0.008 && c.T - a <= 0.018);
    (paired ? mfPos : mfNeg).Add(c.Feat!);
}

Console.WriteLine($"positives: clean={pos.Count} mine_false_real={mfPos.Count} | negatives: mine_false_bad={mfNeg.Count}");

// Balance: subsample positives to ~2x the negative count (deterministic).
var rng = new Random(0);
var allPos = pos.Concat(mfPos).OrderBy(_ => rng.Next()).ToList();
int keepPos = Math.Min(allPos.Count, Math.Max(mfNeg.Count * 2, 50));
var samples = new List<Sample>();
foreach (var f in allPos.Take(keepPos)) samples.Add(new Sample { Features = f, Label = true });
foreach (var f in mfNeg) samples.Add(new Sample { Features = f, Label = false });
Console.WriteLine($"dataset: pos={keepPos} neg={mfNeg.Count} total={samples.Count}");

// Train: min-max normalize -> SDCA logistic regression.
var ml = new MLContext(seed: 0);
IDataView data = ml.Data.LoadFromEnumerable(samples);
var split = ml.Data.TrainTestSplit(data, testFraction: 0.25, seed: 0);
var pipeline = ml.Transforms.NormalizeMinMax("Features")
    .Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression());
var model = pipeline.Fit(split.TrainSet);
var metrics = ml.BinaryClassification.Evaluate(model.Transform(split.TestSet));
Console.WriteLine($"TEST  acc={metrics.Accuracy:F3}  AUC={metrics.AreaUnderRocCurve:F3}  F1={metrics.F1Score:F3}");

// Export ONNX (the gate reads Score.output and applies the logistic itself).
Directory.CreateDirectory(Path.GetDirectoryName(OutOnnx)!);
using (var fs = File.Create(OutOnnx)) ml.Model.ConvertToOnnx(model, data, fs);
Console.WriteLine($"exported {OutOnnx} ({new FileInfo(OutOnnx).Length} bytes)");

// Per-file good-rate sanity: clean watches should stay high, mine_false low.
var pe = ml.Model.CreatePredictionEngine<Sample, Pred>(model);
Console.WriteLine("per-file good-rate:");
foreach (var f in positiveFiles.Append(Path.Combine(SampleDir, "mine_false.wav")))
{
    var evs = Extract(f);
    if (evs.Count == 0) { Console.WriteLine($"  {Path.GetFileName(f),-30} (no events)"); continue; }
    int good = evs.Count(e => pe.Predict(new Sample { Features = e.Feat! }).Probability >= 0.5f);
    Console.WriteLine($"  {Path.GetFileName(f),-30} {good,4}/{evs.Count,-4} = {(double)good / evs.Count:P0}");
}

sealed record Ev(TgEventType Type, double T, float[]? Feat);

sealed class RecordingGate : IBeatEventGate
{
    public string Name => "train";
    public double WindowPreMs { get; }
    public double WindowPostMs { get; }
    public RecordingGate(double pre, double post) { WindowPreMs = pre; WindowPostMs = post; }
    public List<Ev> Events { get; } = new();
    public bool Accept(ReadOnlySpan<float> w, int o, double sr, in BeatCandidate c)
    {
        float[]? feat = null;
        if (!w.IsEmpty)
        {
            var f = new float[BeatWindowFeatures.Points];
            if (BeatWindowFeatures.Extract(w, f)) feat = f;
        }
        Events.Add(new Ev(c.Event.Type, c.Event.TimeSeconds, feat));
        return true;
    }
    public void Reset() { }
}

sealed class Sample
{
    [VectorType(128)] public float[] Features = new float[128];
    public bool Label;
}

sealed class Pred
{
    public bool PredictedLabel;
    public float Probability;
}
