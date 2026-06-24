using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TimeGrapher.Core.Detection.Scoring;

namespace TimeGrapher.Inference;

/// <summary>
/// TinyML implementation of <see cref="IBeatEventGate"/> (the "TinyML socket"):
/// an on-device ONNX signal-quality classifier that vetoes bad-data beat
/// candidates - handling noise, clipping, broadband interference, spurious
/// post-beat impulses - before they reach the metrics, so rate / beat error /
/// amplitude are not polluted by events that should not have been measured.
///
/// It is the AI-feature realization of "Bad Data Rejection" / "Signal Quality
/// Classification" from the project plan. Like every gate it is drop-only and
/// sits downstream of detection: it cannot create or re-time events, and BPH
/// detection and the sync PLL never see its verdicts (structural guarantee of
/// <see cref="IBeatEventGate"/>). A misclassification can therefore only pass
/// or drop a single candidate's metric contribution - it cannot break lock.
///
/// The model classifies the peak-normalized, <see cref="BeatWindowFeatures.Points"/>-point
/// envelope window (<see cref="BeatWindowFeatures"/>, the single feature
/// contract shared with the offline trainer) and the gate accepts a candidate
/// when the modeled probability that it is a good escapement event is at least
/// <c>acceptThreshold</c>.
/// </summary>
public sealed class OnnxBeatEventGate : IBeatEventGate, IDisposable
{
    // ML.NET's ONNX exporter emits the training schema verbatim: the feature
    // column and the label column become graph inputs, and each column also
    // becomes an output. We feed Features (real) plus a dummy Label, and read
    // only Score.output - the raw decision value, a plain float tensor that
    // bypasses the calibrator's ZipMap/sequence path (reading Probability.output
    // instead trips an OnnxRuntime TensorSeq error). Probability is recovered
    // as the logistic of the score.
    private const string FeaturesInput = "Features";
    private const string LabelInput = "Label";
    private const string ScoreOutput = "Score.output";

    // Envelope context captured around each event. The offline trainer extracts
    // the same pre/post window around every labeled event so inference features
    // reproduce the training recipe; changing these is a contract change that
    // requires retraining.
    private const double WindowPreMsConst = 2.0;
    private const double WindowPostMsConst = 6.0;

    private readonly InferenceSession _session;
    private readonly float _acceptThreshold;
    private readonly float[] _features = new float[BeatWindowFeatures.Points];
    private readonly DenseTensor<bool> _dummyLabel = new(new[] { false }, new[] { 1, 1 });
    private readonly string[] _outputs = { ScoreOutput };

    /// <summary>
    /// Loads a classifier from a serialized ONNX model exported by the gate
    /// trainer (ML.NET SDCA logistic regression over the 128-point feature
    /// vector). <paramref name="acceptThreshold"/> is the minimum P(good) in
    /// [0, 1] for a candidate to pass.
    /// </summary>
    public OnnxBeatEventGate(byte[] onnxModel, float acceptThreshold = 0.5f)
    {
        _session = new InferenceSession(onnxModel);
        _acceptThreshold = acceptThreshold;
    }

    /// <summary>
    /// Constructs the gate from the model shipped inside this assembly
    /// (Models/tick-quality.onnx, trained by tools/TimeGrapher.GateTrainer).
    /// </summary>
    public static OnnxBeatEventGate LoadDefault(float acceptThreshold = 0.5f)
    {
        Assembly asm = typeof(OnnxBeatEventGate).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("tick-quality.onnx", StringComparison.Ordinal));
        using Stream stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new OnnxBeatEventGate(ms.ToArray(), acceptThreshold);
    }

    public string Name => "onnx";
    public double WindowPreMs => WindowPreMsConst;
    public double WindowPostMs => WindowPostMsConst;

    public bool Accept(ReadOnlySpan<float> envelopeWindow, int eventOffsetInWindow,
                       double sampleRate, in BeatCandidate candidate)
    {
        // A boundary event arrives with an empty window (offset = -1 sentinel)
        // and carries no envelope evidence to classify; pass it through rather
        // than veto blind - matching PllMatchGate's "no evidence -> keep".
        if (!BeatWindowFeatures.Extract(envelopeWindow, _features))
        {
            return true;
        }

        var input = new DenseTensor<float>(_features, new[] { 1, BeatWindowFeatures.Points });
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(
            new[]
            {
                NamedOnnxValue.CreateFromTensor(FeaturesInput, input),
                NamedOnnxValue.CreateFromTensor(LabelInput, _dummyLabel),
            },
            _outputs);

        float score = results.First().AsEnumerable<float>().First();
        float pGood = 1f / (1f + MathF.Exp(-score));
        return pGood >= _acceptThreshold;
    }

    public void Reset()
    {
    }

    public void Dispose() => _session.Dispose();
}
