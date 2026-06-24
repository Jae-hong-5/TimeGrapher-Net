using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TimeGrapher.Core.Detection.Scoring;

namespace TimeGrapher.Inference;

/// <summary>
/// <see cref="IBeatLandmarkRefiner"/> backed by an ONNX model, run with ONNX
/// Runtime. This is the TinyML refiner the plan reserves
/// (docs/for-ai/TINYML_LANDMARK_REFINER_PLAN.md): it lives in this leaf project
/// so Core stays free of any ML runtime, and is injected at the composition root
/// (the verifier's <c>--landmark=onnx:&lt;path&gt;</c>).
///
/// <para>Model IO contract (matches the TrainingDataExporter rows):</para>
/// <list type="bullet">
/// <item>input: float32 <c>[1, N]</c> - an envelope (ProcessedPcm) window of N
/// samples at the analysis sample rate, anchored so the detector A sits
/// <see cref="WindowPreMs"/> into it. N is read from the model when static,
/// else derived as (pre+post) ms at the call's sample rate.</item>
/// <item>output: float32 <c>[1, K]</c>, K &gt;= 2, =
/// <c>[a_off, c_off, a_conf, c_conf, (b_as_a_risk, b_as_c_risk)]</c> where the
/// offsets are in samples measured from the window start.</item>
/// </list>
///
/// <para>Fail-open: an unavailable window, a malformed output, or any inference
/// exception returns <see cref="BeatLandmarkRefinement.Fallback"/>, so a broken
/// or overconfident model degrades to the raw detector values instead of
/// crashing or corrupting metrics. The host still clamps and applies the
/// confidence floor on top.</para>
/// </summary>
public sealed class OnnxBeatLandmarkRefiner : IBeatLandmarkRefiner, IDisposable
{
    private const double DefaultWindowPreMs = 6.0;
    private const double DefaultWindowPostMs = 14.0;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _staticInputLen; // 0 when the model input length is dynamic

    public OnnxBeatLandmarkRefiner(string modelPath,
                                   double windowPreMs = DefaultWindowPreMs,
                                   double windowPostMs = DefaultWindowPostMs)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("ONNX landmark model not found", modelPath);
        }
        _session = new InferenceSession(modelPath);
        WindowPreMs = windowPreMs;
        WindowPostMs = windowPostMs;
        _inputName = _session.InputMetadata.Keys.First();
        _staticInputLen = StaticLength(_session.InputMetadata[_inputName].Dimensions);
    }

    public string Name => "onnx";
    public double WindowPreMs { get; }
    public double WindowPostMs { get; }

    public BeatLandmarkRefinement Refine(ReadOnlySpan<float> envelopeWindow, int aOffsetInWindow,
                                         int cOffsetInWindow, double sampleRate, in BeatLandmarkCandidate candidate)
    {
        if (aOffsetInWindow < 0 || envelopeWindow.IsEmpty)
        {
            return BeatLandmarkRefinement.Fallback;
        }

        int preSamples = (int)Math.Round(WindowPreMs * 1e-3 * sampleRate);
        int n = _staticInputLen > 0
            ? _staticInputLen
            : (int)Math.Round((WindowPreMs + WindowPostMs) * 1e-3 * sampleRate);
        int start = aOffsetInWindow - preSamples;
        if (n <= 0 || start < 0 || start + n > envelopeWindow.Length)
        {
            return BeatLandmarkRefinement.Fallback;
        }

        var input = new DenseTensor<float>(new[] { 1, n });
        for (int i = 0; i < n; i++)
        {
            input[0, i] = envelopeWindow[start + i];
        }

        float[] output;
        try
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });
            output = results.First().AsEnumerable<float>().ToArray();
        }
        catch
        {
            return BeatLandmarkRefinement.Fallback; // fail-open on any inference error
        }

        if (output.Length < 2)
        {
            return BeatLandmarkRefinement.Fallback;
        }
        return MapOutputToRefinement(output, candidate, aOffsetInWindow);
    }

    /// <summary>
    /// Pure decode: model output (A/C offsets in window samples, then optional
    /// confidences) plus the beat's window position become absolute corrected
    /// A/C samples. The window starts at <c>candidate.ASample - aOffsetInWindow</c>,
    /// matching how TrainingDataExporter labels offsets. Exposed for unit testing.
    /// </summary>
    public static BeatLandmarkRefinement MapOutputToRefinement(
        ReadOnlySpan<float> output, in BeatLandmarkCandidate candidate, int aOffsetInWindow)
    {
        double windowStart = candidate.ASample - aOffsetInWindow;
        double correctedA = windowStart + output[0];
        double correctedC = windowStart + output[1];
        float aConf = output.Length > 2 ? output[2] : 1.0f;
        float cConf = output.Length > 3 ? output[3] : 1.0f;
        return new BeatLandmarkRefinement(
            Accepted: true,
            CorrectedC: true, CorrectedCSample: correctedC, CConfidence: cConf,
            CorrectedA: true, CorrectedASample: correctedA, AConfidence: aConf);
    }

    public void Reset()
    {
    }

    public void Dispose() => _session.Dispose();

    // Product of the positive (statically-known) dimensions; 0 when only the
    // batch dim is known (i.e. the input length is dynamic).
    private static int StaticLength(int[] dimensions)
    {
        int len = 1;
        foreach (int d in dimensions)
        {
            if (d > 0) len *= d;
        }
        return len > 1 ? len : 0;
    }
}
