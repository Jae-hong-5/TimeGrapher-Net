using System.Globalization;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;

// Advisory signal-quality classifier trainer (UC1). Generates fully synthetic,
// self-labelled training data and exports an ONNX multiclass model that the
// runtime leaf (TimeGrapher.Inference) loads. See the csproj header for the why.
//
// Pipeline:
//   1. Sweep a grid of WatchSynthStream configs spanning clean/noisy/weak/unstable.
//   2. Stream each through DetectorMetricsEngine wired with the heuristic classifier
//      and collect every emitted (SignalQualityFeatures, SignalQualityClass) window.
//   3. Train an ML.NET SDCA maximum-entropy multiclass model on the 8-feature vector.
//   4. Export ONNX + a class-order sidecar (Score-index -> class name).
//   5. Self-verify: reload the ONNX with ONNX Runtime and confirm it reproduces the
//      ML.NET prediction on held-out rows (the train->serve round-trip gate).

const int Block = 4096;
const int SampleRate = 48000;
const double LiftAngle = 52.0;
const string OutDir = "src/TimeGrapher.Inference/Models";
const string OutOnnx = OutDir + "/signal-quality.onnx";
const string OutClasses = OutDir + "/signal-quality.classes.txt";

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// ---- 1. Recipe grid ------------------------------------------------------
// Each recipe is one synthetic stream. We do NOT label by knob (the user chose
// heuristic labels): the knobs only need to push the feature distribution across
// all four classes; the heuristic assigns the ground truth.
var recipes = new List<Recipe>();
int seed = 0;

double[] pcms = { 0.60, 0.35, 0.18, 0.09, 0.05, 0.03 };
double[] noises = { 0.006, 0.02, 0.05, 0.09, 0.13 };
int[] bphs = { 18000, 21600, 28800 };

// Amplitude x noise grid at a couple of beat rates, both packet profiles.
foreach (int bph in new[] { 21600, 28800 })
foreach (bool realistic in new[] { false, true })
foreach (double pcm in pcms)
foreach (double noise in noises)
{
    recipes.Add(new Recipe($"grid-{bph}-{(realistic ? "r" : "c")}-p{pcm}-n{noise}",
        bph, pcm, noise, JitterUs: 0.0, BeatErrorMs: 0.0,
        ImpulseRate: 0.0, ImpulseLevel: 0.0, Realistic: realistic, Seed: (ulong)seed++));
}

// Weak-but-trackable signal: low PCM level with low noise so the detector still
// locks (stable timing) while SNR / peak-margin sit in the heuristic's weak band.
// This is a narrow regime - if the signal is weak AND noisy the timing collapses
// and the heuristic (rightly) labels it Unstable instead - so it needs its own
// targeted sweep, mirroring the proven weak-1/weak-2 adverse rows. The Realistic
// profile's beat-to-beat amplitude variation also pushes PeakLevelCv.
foreach (int bph in new[] { 18000, 21600 })
foreach (bool realistic in new[] { false, true })
foreach (double pcm in new[] { 0.035, 0.05, 0.065, 0.08 })
foreach (double noise in new[] { 0.006, 0.010, 0.014 })
{
    recipes.Add(new Recipe($"weak-{bph}-{(realistic ? "r" : "c")}-p{pcm}-n{noise}",
        bph, pcm, noise, JitterUs: 0.0, BeatErrorMs: 0.0,
        ImpulseRate: 0.0, ImpulseLevel: 0.0, Realistic: realistic, Seed: (ulong)seed++));
}

// Timing instability: heavy interval jitter and/or beat error (push IntervalJitterCv).
foreach (int bph in bphs)
foreach (double jitter in new[] { 8000.0, 16000.0, 26000.0 })
{
    recipes.Add(new Recipe($"jitter-{bph}-{jitter}",
        bph, Pcm: 0.30, Noise: 0.012, JitterUs: jitter, BeatErrorMs: 0.0,
        ImpulseRate: 0.0, ImpulseLevel: 0.0, Realistic: false, Seed: (ulong)seed++));
}
foreach (double be in new[] { 4.0, 8.0 })
{
    recipes.Add(new Recipe($"beaterr-{be}",
        Bph: 21600, Pcm: 0.30, Noise: 0.012, JitterUs: 6000.0, BeatErrorMs: be,
        ImpulseRate: 0.0, ImpulseLevel: 0.0, Realistic: false, Seed: (ulong)seed++));
}

// Impulse storms: missed beats / sync loss (push MissedBeatRate / SyncLossRate).
recipes.Add(new Recipe("impulse-dos", 21600, Pcm: 0.04, Noise: 0.004,
    JitterUs: 0.0, BeatErrorMs: 0.0, ImpulseRate: 1.0, ImpulseLevel: 0.95, Realistic: false, Seed: (ulong)seed++));
recipes.Add(new Recipe("impulse-storm", 28800, Pcm: 0.25, Noise: 0.02,
    JitterUs: 0.0, BeatErrorMs: 0.0, ImpulseRate: 3.0, ImpulseLevel: 0.6, Realistic: false, Seed: (ulong)seed++));
recipes.Add(new Recipe("impulse-mid", 21600, Pcm: 0.12, Noise: 0.03,
    JitterUs: 4000.0, BeatErrorMs: 0.0, ImpulseRate: 2.0, ImpulseLevel: 0.7, Realistic: true, Seed: (ulong)seed++));

const int Seconds = 10;
Console.WriteLine($"recipes: {recipes.Count}  (~{Seconds}s each @ {SampleRate} Hz)");

// ---- 2. Generate features + heuristic labels -----------------------------
var samples = new List<Sample>();
foreach (Recipe r in recipes)
{
    samples.AddRange(Collect(r));
}

Report("synth-derived", samples);

// Boundary augmentation. The synth + detector path rarely produces a clean
// "weak but trackable" window (a signal weak enough to trip the weak band yet
// stable enough not to be labelled Unstable is a narrow regime), so the weak
// class is starved. We top up coverage by sampling feature vectors across the
// realistic ranges and labelling them with the SAME heuristic - i.e. we distil
// the heuristic's decision surface, which is exactly UC1's honest goal: a
// learned, on-device approximation of the heuristic over a realistic feature
// distribution. The synth-derived rows keep the distribution grounded; the
// augmentation guarantees every class (especially WeakSignal) is represented.
var heuristic = new HeuristicSignalQualityClassifier();
samples.AddRange(Augment(8000, new Random(7), heuristic));
Report("after augmentation", samples);

// Balance: downsample every class to the smallest class so the trainer is not
// biased toward the majority (Noisy). Deterministic shuffle (seeded).
var balanceRng = new Random(11);
int cap = samples.GroupBy(s => s.Label).Min(g => g.Count());
samples = samples
    .GroupBy(s => s.Label)
    .SelectMany(g => g.OrderBy(_ => balanceRng.Next()).Take(cap))
    .OrderBy(_ => balanceRng.Next())
    .ToList();
Report($"balanced (cap {cap}/class)", samples);

if (samples.GroupBy(s => s.Label).Count() < 2)
{
    Console.Error.WriteLine("ERROR: need at least two classes to train a multiclass model.");
    return 1;
}

// ---- 3. Train ML.NET multiclass ------------------------------------------
var ml = new MLContext(seed: 0);
IDataView data = ml.Data.LoadFromEnumerable(samples);
DataOperationsCatalog.TrainTestData split = ml.Data.TrainTestSplit(data, testFraction: 0.2, seed: 0);

var pipeline =
    ml.Transforms.Conversion.MapValueToKey("LabelKey", nameof(Sample.Label))
    .Append(ml.Transforms.NormalizeMinMax(nameof(Sample.Features)))
    .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(
        labelColumnName: "LabelKey", featureColumnName: nameof(Sample.Features)))
    .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

ITransformer model = pipeline.Fit(split.TrainSet);

MulticlassClassificationMetrics metrics =
    ml.MulticlassClassification.Evaluate(model.Transform(split.TestSet), labelColumnName: "LabelKey");
Console.WriteLine($"TEST  microAcc={metrics.MicroAccuracy:F3}  macroAcc={metrics.MacroAccuracy:F3}  logLoss={metrics.LogLoss:F3}");

// Class order = the Score column's slot names (Score index -> class name).
string[] classOrder = ScoreSlotNames(model, data.Schema);
Console.WriteLine($"class order (Score index -> name): [{string.Join(", ", classOrder)}]");

// ---- 4. Export ONNX + class-order sidecar --------------------------------
Directory.CreateDirectory(OutDir);
using (FileStream fs = File.Create(OutOnnx))
{
    ml.Model.ConvertToOnnx(model, data, fs);
}
File.WriteAllLines(OutClasses, classOrder);
Console.WriteLine($"exported {OutOnnx} ({new FileInfo(OutOnnx).Length} bytes)");
Console.WriteLine($"exported {OutClasses}");

// ---- 5. Self-verify the ONNX round-trip ----------------------------------
using var session = new InferenceSession(File.ReadAllBytes(OutOnnx));
Console.WriteLine("ONNX inputs:");
foreach (KeyValuePair<string, NodeMetadata> kv in session.InputMetadata)
{
    Console.WriteLine($"  {kv.Key,-16} {kv.Value.ElementType.Name}[{string.Join(",", kv.Value.Dimensions)}]");
}
Console.WriteLine("ONNX outputs:");
foreach (KeyValuePair<string, NodeMetadata> kv in session.OutputMetadata)
{
    Console.WriteLine($"  {kv.Key,-16} {kv.Value.ElementType.Name}[{string.Join(",", kv.Value.Dimensions)}]");
}

// The float, per-class score tensor. ML.NET names it "Score.output" (the column
// "Score" suffixed by the ONNX exporter). Pick the float output whose flattened
// length matches the class count.
string scoreOutput = session.OutputMetadata
    .First(kv => kv.Value.ElementType == typeof(float) &&
                 kv.Value.Dimensions.Where(d => d > 0).Aggregate(1, (a, b) => a * b) == classOrder.Length).Key;
Console.WriteLine($"using score output: {scoreOutput}");

PredictionEngine<Sample, MlPrediction> mlEngine = ml.Model.CreatePredictionEngine<Sample, MlPrediction>(model);

int agree = 0, total = 0;
var verifyRng = new Random(1);
foreach (Sample s in samples.OrderBy(_ => verifyRng.Next()).Take(2000))
{
    string mlClass = mlEngine.Predict(s).PredictedLabel;
    string onnxClass = OnnxPredict(session, scoreOutput, classOrder, s.Features, out float conf);
    total++;
    if (mlClass == onnxClass)
    {
        agree++;
    }
}
double agreement = total == 0 ? 0.0 : (double)agree / total;
Console.WriteLine($"ONNX vs ML.NET agreement on {total} rows: {agreement:P2}");
if (agreement < 0.99)
{
    Console.Error.WriteLine("ERROR: ONNX round-trip disagrees with ML.NET (>1% mismatch). The inference recipe is wrong.");
    return 2;
}

Console.WriteLine("OK");
return 0;

// ==========================================================================

// Stream one recipe through the shared engine + heuristic; yield labelled windows.
IEnumerable<Sample> Collect(Recipe r)
{
    WatchSynthStreamConfig cfg = r.Realistic
        ? WatchSynthStreamConfig.Realistic()
        : WatchSynthStreamConfig.Clean();
    cfg.SampleRateHz = SampleRate;
    cfg.Bph = r.Bph;
    cfg.PcmPeakSignalLevel = r.Pcm;
    cfg.NoisePeakSignalLevel = r.Noise;
    cfg.TimingJitterUs = r.JitterUs;
    cfg.BeatErrorMs = r.BeatErrorMs;
    cfg.Seed = r.Seed;
    if (r.ImpulseRate > 0.0)
    {
        cfg.ImpulseNoiseRatePerSecond = r.ImpulseRate;
        cfg.ImpulseNoisePeakSignalLevel = r.ImpulseLevel;
    }

    var synth = new WatchSynthStream(cfg);
    var engine = new DetectorMetricsEngine(
        new DetectorMetricsEngineConfig(
            SampleRate: SampleRate, LiftAngle: LiftAngle, AveragingPeriod: 2,
            UseCOnset: false, AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0),
        new HeuristicSignalQualityClassifier());

    var rows = new List<Sample>();
    var block = new float[Block];
    long generated = 0;
    long totalSamples = (long)SampleRate * Seconds;
    while (generated < totalSamples)
    {
        int slice = (int)Math.Min(block.Length, totalSamples - generated);
        Span<float> span = block.AsSpan(0, slice);
        synth.Generate(span);
        DetectorMetricsBlockUpdate update = engine.Process(span);
        Add(update, rows);
        generated += slice;
    }
    Add(engine.Flush(), rows);
    return rows;
}

static void Report(string title, List<Sample> rows)
{
    Console.WriteLine($"{title}: {rows.Count} rows");
    foreach (IGrouping<string, Sample> g in rows.GroupBy(s => s.Label).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  {g.Key,-12} {g.Count(),6}");
    }
}

// Sample heuristic-labelled feature vectors across realistic ranges to cover the
// decision surface, especially the starved WeakSignal region. A fraction target
// the unstable corner (high jitter / missed / sync-loss); the rest stay timing-
// stable so SNR and peak-margin decide between Good / Noisy / WeakSignal.
static IEnumerable<Sample> Augment(int n, Random rng, HeuristicSignalQualityClassifier heuristic)
{
    for (int i = 0; i < n; i++)
    {
        bool unstable = rng.NextDouble() < 0.30;
        float snrDb = (float)(rng.NextDouble() * 36.0);              // spans weak(<12)/noisy(<24)/good
        float peakMargin = (float)(0.8 + rng.NextDouble() * 3.2);    // weak when < 1.5
        float noiseFloor = (float)(0.0005 + rng.NextDouble() * 0.05);
        float intervalCv = unstable ? (float)(0.05 + rng.NextDouble() * 0.10) : (float)(rng.NextDouble() * 0.04);
        float peakCv = (float)(rng.NextDouble() * 0.45);             // noisy when > 0.25
        float missedRate = unstable ? (float)(rng.NextDouble() * 0.15) : 0f;
        float syncLossRate = unstable ? (float)(rng.NextDouble() * 0.08) : 0f;
        float syncedFraction = (float)(0.6 + rng.NextDouble() * 0.4); // >= 0.5 so never Unknown

        var f = new SignalQualityFeatures(
            snrDb, peakMargin, noiseFloor, intervalCv, peakCv, missedRate, syncLossRate, syncedFraction);
        SignalQualityClass cls = heuristic.Classify(f).Class;
        if (cls == SignalQualityClass.Unknown)
        {
            continue;
        }
        yield return new Sample { Features = ToVector(f), Label = cls.ToString() };
    }
}

static void Add(DetectorMetricsBlockUpdate update, List<Sample> rows)
{
    // Skip Unknown (pre-sync / warming up): SignalQualityFlagsMap maps it to no
    // warning, and it is not an actionable class to learn.
    if (update.Result.QualityAssessment is { } a && a.Class != SignalQualityClass.Unknown)
    {
        rows.Add(new Sample { Features = ToVector(a.Features), Label = a.Class.ToString() });
    }
}

static float[] ToVector(in SignalQualityFeatures f) => new[]
{
    f.SnrDb, f.PeakMarginRatio, f.NoiseFloorLevel, f.IntervalJitterCv,
    f.PeakLevelCv, f.MissedBeatRate, f.SyncLossRate, f.SyncedFraction,
};

static string[] ScoreSlotNames(ITransformer model, DataViewSchema inputSchema)
{
    DataViewSchema outSchema = model.GetOutputSchema(inputSchema);
    DataViewSchema.Column score = outSchema["Score"];
    VBuffer<ReadOnlyMemory<char>> slots = default;
    score.GetSlotNames(ref slots);
    return slots.DenseValues().Select(v => v.ToString()).ToArray();
}

static string OnnxPredict(InferenceSession session, string scoreOutput, string[] classOrder,
                          float[] features, out float confidence)
{
    var inputs = new List<NamedOnnxValue>();
    foreach (KeyValuePair<string, NodeMetadata> kv in session.InputMetadata)
    {
        if (kv.Key.Contains("Features", StringComparison.OrdinalIgnoreCase))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(kv.Key,
                new DenseTensor<float>(features, new[] { 1, features.Length })));
        }
        else
        {
            // The ML.NET exporter emits the training label column as a graph input;
            // feed a harmless dummy so only the score path is exercised.
            inputs.Add(MakeDummy(kv.Key, kv.Value));
        }
    }

    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
        session.Run(inputs, new[] { scoreOutput });
    float[] scores = results.First().AsEnumerable<float>().ToArray();
    int best = ArgMax(scores);
    confidence = Softmax(scores)[best];
    return classOrder[best];
}

static NamedOnnxValue MakeDummy(string name, NodeMetadata meta)
{
    var shape = meta.Dimensions.Select(d => d > 0 ? d : 1).ToArray();
    if (shape.Length == 0)
    {
        shape = new[] { 1, 1 };
    }
    int len = shape.Aggregate(1, (a, b) => a * b);
    Type t = meta.ElementType;
    if (t == typeof(float)) return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(new float[len], shape));
    if (t == typeof(long)) return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(new long[len], shape));
    if (t == typeof(int)) return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(new int[len], shape));
    if (t == typeof(bool)) return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<bool>(new bool[len], shape));
    string[] str = Enumerable.Repeat(string.Empty, len).ToArray();
    return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<string>(str, shape));
}

static int ArgMax(float[] v)
{
    int best = 0;
    for (int i = 1; i < v.Length; i++)
    {
        if (v[i] > v[best])
        {
            best = i;
        }
    }
    return best;
}

static float[] Softmax(float[] v)
{
    float max = v.Max();
    var e = new float[v.Length];
    float sum = 0f;
    for (int i = 0; i < v.Length; i++)
    {
        e[i] = MathF.Exp(v[i] - max);
        sum += e[i];
    }
    for (int i = 0; i < v.Length; i++)
    {
        e[i] /= sum;
    }
    return e;
}

sealed record Recipe(
    string Name, int Bph, double Pcm, double Noise, double JitterUs, double BeatErrorMs,
    double ImpulseRate, double ImpulseLevel, bool Realistic, ulong Seed);

sealed class Sample
{
    [VectorType(8)]
    public float[] Features = new float[8];
    public string Label = string.Empty;
}

sealed class MlPrediction
{
    public string PredictedLabel = string.Empty;
}
