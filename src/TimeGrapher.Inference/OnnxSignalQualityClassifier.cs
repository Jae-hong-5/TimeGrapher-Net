using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Inference;

/// <summary>
/// TinyML implementation of <see cref="ISignalQualityClassifier"/> (the "TinyML
/// socket"): an on-device ONNX multiclass model that labels the live
/// <see cref="SignalQualityFeatures"/> window Good / Noisy / WeakSignal /
/// Unstable. It is the AI-feature realization of "Signal Quality Classification"
/// from the project plan.
///
/// Like the whole signal-quality path it is <b>advisory</b>: the verdict only
/// annotates how far to trust the live readings (it rides <c>AnalysisFrame
/// .SignalQuality</c> and surfaces through SignalQualityFlagsMap). It never drops
/// or alters a beat, so a misclassification cannot move rate / beat error /
/// amplitude - in deliberate contrast to the removed event veto.
///
/// The model is a learned approximation of <see cref="HeuristicSignalQualityClassifier"/>
/// over a realistic feature distribution (see tools/TimeGrapher.SignalQualityTrainer).
/// The class order is data-dependent, so it is read from the embedded
/// class-order sidecar rather than hardcoded. ONNX Runtime never leaks into Core:
/// Core defines the seam, this leaf project implements it, and the App injects it
/// at the composition root.
/// </summary>
public sealed class OnnxSignalQualityClassifier : ISignalQualityClassifier, IDisposable
{
    private const string ModelResource = "signal-quality.onnx";
    private const string ClassesResource = "signal-quality.classes.txt";

    // Mirrors HeuristicSignalQualityClassifier's pre-sync guard: below half-synced
    // the window is warming up and the model would be out of its training
    // distribution, so report Unknown (no warning) exactly as the heuristic does.
    private const float SyncedFloor = 0.5f;

    private readonly InferenceSession _session;
    private readonly SignalQualityClass[] _classOrder; // Score index -> class
    private readonly string _featuresInput;
    private readonly string[] _scoreOutput;
    private readonly NamedOnnxValue[] _auxInputs; // non-feature graph inputs (dummy-fed)
    private readonly float[] _features = new float[8];

    /// <summary>
    /// Builds a classifier from a serialized ONNX model and the class order it was
    /// exported with (Score-index -> class name). Throws if the model is unreadable
    /// or its I/O shape does not match <paramref name="classOrder"/>; the composition
    /// root catches that and falls back to the heuristic.
    /// </summary>
    public OnnxSignalQualityClassifier(byte[] onnxModel, IReadOnlyList<string> classOrder)
    {
        _session = new InferenceSession(onnxModel);
        _classOrder = classOrder.Select(Enum.Parse<SignalQualityClass>).ToArray();

        _featuresInput = _session.InputMetadata.Keys
            .First(k => k.Contains("Features", StringComparison.OrdinalIgnoreCase));

        // The ML.NET ONNX exporter emits the training label column as a graph input;
        // it plays no role in scoring, so feed a harmless zero/empty dummy for every
        // non-feature input (built once, reused per call).
        _auxInputs = _session.InputMetadata
            .Where(kv => kv.Key != _featuresInput)
            .Select(kv => Dummy(kv.Key, kv.Value))
            .ToArray();

        // The per-class score vector: the single float output whose length is the
        // class count. We read raw scores and softmax them ourselves (the ZipMap/
        // probability path is fragile under ONNX Runtime).
        string score = _session.OutputMetadata
            .First(kv => kv.Value.ElementType == typeof(float) && FlatLength(kv.Value) == _classOrder.Length)
            .Key;
        _scoreOutput = new[] { score };
    }

    /// <summary>
    /// Returns <paramref name="primary"/>() or, if it throws (model missing /
    /// unreadable / shape mismatch), <paramref name="fallback"/>(). This is the
    /// explicitly-requested graceful degradation: a model problem must never crash
    /// startup - the advisory feature simply falls back to the heuristic Strategy.
    /// The catch is broad on purpose (any load failure degrades the same way).
    /// </summary>
    public static ISignalQualityClassifier LoadOrElse(
        Func<ISignalQualityClassifier> primary, Func<ISignalQualityClassifier> fallback)
    {
        try
        {
            return primary();
        }
        catch (Exception)
        {
            return fallback();
        }
    }

    /// <summary>Builds the classifier from the model + sidecar shipped inside this assembly.</summary>
    public static OnnxSignalQualityClassifier LoadDefault()
    {
        Assembly asm = typeof(OnnxSignalQualityClassifier).Assembly;
        byte[] model = ReadBytes(asm, ModelResource);
        string[] classes = ReadText(asm, ClassesResource)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        return new OnnxSignalQualityClassifier(model, classes);
    }

    public SignalQualityAssessment Classify(in SignalQualityFeatures features)
    {
        if (features.SyncedFraction < SyncedFloor)
        {
            return SignalQualityAssessment.Unknown;
        }

        _features[0] = features.SnrDb;
        _features[1] = features.PeakMarginRatio;
        _features[2] = features.NoiseFloorLevel;
        _features[3] = features.IntervalJitterCv;
        _features[4] = features.PeakLevelCv;
        _features[5] = features.MissedBeatRate;
        _features[6] = features.SyncLossRate;
        _features[7] = features.SyncedFraction;

        var inputs = new List<NamedOnnxValue>(_auxInputs.Length + 1);
        inputs.AddRange(_auxInputs);
        inputs.Add(NamedOnnxValue.CreateFromTensor(
            _featuresInput, new DenseTensor<float>(_features, new[] { 1, _features.Length })));

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
            _session.Run(inputs, _scoreOutput);

        float[] scores = results.First().AsEnumerable<float>().ToArray();
        int best = ArgMax(scores);
        float confidence = Softmax(scores, best);
        return new SignalQualityAssessment(_classOrder[best], confidence, features);
    }

    public void Dispose() => _session.Dispose();

    private static int FlatLength(NodeMetadata meta) =>
        meta.Dimensions.Where(d => d > 0).Aggregate(1, (a, b) => a * b);

    private static NamedOnnxValue Dummy(string name, NodeMetadata meta)
    {
        int[] shape = meta.Dimensions.Select(d => d > 0 ? d : 1).ToArray();
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
        return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<string>(
            Enumerable.Repeat(string.Empty, len).ToArray(), shape));
    }

    private static int ArgMax(float[] v)
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

    // Softmax probability of the winning class (numerically stable).
    private static float Softmax(float[] scores, int index)
    {
        float max = scores[0];
        for (int i = 1; i < scores.Length; i++)
        {
            if (scores[i] > max)
            {
                max = scores[i];
            }
        }
        float sum = 0f;
        for (int i = 0; i < scores.Length; i++)
        {
            sum += MathF.Exp(scores[i] - max);
        }
        return MathF.Exp(scores[index] - max) / sum;
    }

    private static byte[] ReadBytes(Assembly asm, string suffix)
    {
        using Stream s = OpenResource(asm, suffix);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ReadText(Assembly asm, string suffix)
    {
        using Stream s = OpenResource(asm, suffix);
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }

    private static Stream OpenResource(Assembly asm, string suffix)
    {
        string name = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        return asm.GetManifestResourceStream(name)!;
    }
}
