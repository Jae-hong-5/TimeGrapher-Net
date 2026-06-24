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
/// contract shared with the offline training pipeline) and emits the
/// probability that the candidate is a good escapement event; a candidate is
/// accepted when that probability is at least <c>acceptThreshold</c>.
/// </summary>
public sealed class OnnxBeatEventGate : IBeatEventGate, IDisposable
{
    // Envelope context captured around each event. The offline trainer extracts
    // the same pre/post window around every labeled event so the inference
    // features reproduce the training recipe; changing these here is a contract
    // change that requires retraining.
    private const double WindowPreMsConst = 2.0;
    private const double WindowPostMsConst = 6.0;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly float _acceptThreshold;
    private readonly float[] _features = new float[BeatWindowFeatures.Points];

    /// <summary>
    /// Loads a classifier from a serialized ONNX model. The model takes a
    /// [1, <see cref="BeatWindowFeatures.Points"/>] float input and produces a
    /// single float output: the probability in [0, 1] that the candidate is a
    /// good escapement event.
    /// </summary>
    public OnnxBeatEventGate(byte[] onnxModel, float acceptThreshold = 0.5f)
    {
        _session = new InferenceSession(onnxModel);
        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
        _acceptThreshold = acceptThreshold;
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
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
            _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });
        float pGood = results.First().AsEnumerable<float>().First();
        return pGood >= _acceptThreshold;
    }

    public void Reset()
    {
    }

    public void Dispose() => _session.Dispose();
}
