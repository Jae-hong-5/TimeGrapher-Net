namespace TimeGrapher.Core.Detection;

/// <summary>
/// Per-sample output of the <see cref="ScopeFilters"/> bank: the four
/// Multi-Filter Scope views (plan: PC-RM4 F0..F3) of the same input sample.
/// </summary>
public readonly record struct ScopeFilterSample(double F0, double F1, double F2, double F3);

/// <summary>
/// F2/F3 building block: emphasizes rising slopes and attenuates falling ones.
/// Formulation (chosen for stability — every value stays bounded by the input):
/// a rising sample (value &gt;= previous, so plateaus count as rises) passes
/// through unchanged and re-arms the attenuator at that local level; a falling
/// sample first decays the attenuator by the per-sample factor and then outputs
/// max(value - attenuator, 0). Right after a local rise the attenuator sits at
/// the peak, so the following decay is fully suppressed and only re-admitted as
/// the attenuator halves away — sharp upward features (T3, and to some extent
/// T2/T1) stay prominent while their decays fade.
/// </summary>
public sealed class RisingEdgeEmphasis
{
    private readonly double _fallDecay;
    private double _previous;
    private double _attenuation;

    /// <param name="fallDecay">Per-sample attenuator decay factor in (0, 1).</param>
    public RisingEdgeEmphasis(double fallDecay)
    {
        _fallDecay = fallDecay;
    }

    public double Process(double value)
    {
        double output;
        if (value >= _previous)
        {
            output = value;
            _attenuation = value;
        }
        else
        {
            _attenuation *= _fallDecay;
            output = Math.Max(value - _attenuation, 0.0);
        }

        _previous = value;
        return output;
    }

    public void Reset()
    {
        _previous = 0.0;
        _attenuation = 0.0;
    }
}

/// <summary>
/// Streaming filter bank for the Scope Function with Multiple Filter Views
/// (plan: the PC-RM4 F0..F3 scope filters). One <see cref="Process"/> call per
/// raw sample produces all four views in O(1) with no allocation, so the bank
/// can run inside the analysis hot path (125 ms/beat budget):
/// <list type="bullet">
/// <item><b>F0</b> — the signal as captured, mirrored around its average:
/// |x - mean| with a slow one-pole running mean (time constant
/// <see cref="MeanTimeConstantS"/> ≈ 50 ms, sample-rate-derived), so both
/// positive and negative excursions reflect symmetrically.</item>
/// <item><b>F1</b> — moving average of F0 over a
/// <see cref="SmoothingWindowS"/> ≈ 0.5 ms window (sample-rate-derived,
/// ring-buffer running sum): a smoother, less noisy trace. Note: averaging
/// also fades low-amplitude signal detail, as the plan warns.</item>
/// <item><b>F2</b> — F1 through <see cref="RisingEdgeEmphasis"/> with a
/// <see cref="FallHalfLifeS"/> ≈ 5 ms attenuator half-life
/// (sample-rate-derived): rises kept, decays suppressed; reveals T3 and
/// somewhat T2.</item>
/// <item><b>F3</b> — only the upper portion relative to the average,
/// u = max(x - mean, 0), through its own <see cref="RisingEdgeEmphasis"/>;
/// useful for identifying T1 and especially T3.</item>
/// </list>
/// </summary>
public sealed class ScopeFilters
{
    /// <summary>F0/F3 running-mean time constant (seconds), ~50 ms.</summary>
    public const double MeanTimeConstantS = 0.050;

    /// <summary>F1 moving-average window (seconds), ~0.5 ms.</summary>
    public const double SmoothingWindowS = 0.0005;

    /// <summary>F2/F3 falling-slope attenuator half-life (seconds), ~5 ms.</summary>
    public const double FallHalfLifeS = 0.005;

    private readonly double _meanAlpha;
    private double _mean;
    private bool _meanPrimed;

    private readonly double[] _smoothingWindow;
    private double _smoothingSum;
    private int _smoothingPos;
    private int _smoothingCount;

    private readonly RisingEdgeEmphasis _f2Edge;
    private readonly RisingEdgeEmphasis _f3Edge;

    public ScopeFilters(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        }

        _meanAlpha = 1.0 - Math.Exp(-1.0 / (MeanTimeConstantS * sampleRate));
        _smoothingWindow = new double[SmoothingWindowLength(sampleRate)];
        double fallDecay = FallDecayPerSample(sampleRate);
        _f2Edge = new RisingEdgeEmphasis(fallDecay);
        _f3Edge = new RisingEdgeEmphasis(fallDecay);
    }

    /// <summary>F1 window length in samples for <paramref name="sampleRate"/> (≥ 1).</summary>
    public static int SmoothingWindowLength(int sampleRate) =>
        Math.Max(1, (int)Math.Round(SmoothingWindowS * sampleRate));

    /// <summary>Per-sample attenuator factor giving the ~5 ms half-life at <paramref name="sampleRate"/>.</summary>
    public static double FallDecayPerSample(int sampleRate) =>
        Math.Pow(0.5, 1.0 / (FallHalfLifeS * sampleRate));

    public ScopeFilterSample Process(double x)
    {
        // The mean primes on the first sample so a constant input has no
        // startup transient; afterwards it tracks with the slow time constant.
        if (!_meanPrimed)
        {
            _mean = x;
            _meanPrimed = true;
        }
        else
        {
            _mean += _meanAlpha * (x - _mean);
        }

        double f0 = Math.Abs(x - _mean);

        // F1 ring buffer: O(1) running sum; until the window fills, average
        // over the samples seen so far (no initial dip).
        _smoothingSum -= _smoothingWindow[_smoothingPos];
        _smoothingWindow[_smoothingPos] = f0;
        _smoothingSum += f0;
        _smoothingPos++;
        if (_smoothingPos == _smoothingWindow.Length)
        {
            _smoothingPos = 0;
        }
        if (_smoothingCount < _smoothingWindow.Length)
        {
            _smoothingCount++;
        }
        double f1 = _smoothingSum / _smoothingCount;

        double f2 = _f2Edge.Process(f1);
        double f3 = _f3Edge.Process(Math.Max(x - _mean, 0.0));
        return new ScopeFilterSample(f0, f1, f2, f3);
    }

    public void Reset()
    {
        _mean = 0.0;
        _meanPrimed = false;
        Array.Clear(_smoothingWindow);
        _smoothingSum = 0.0;
        _smoothingPos = 0;
        _smoothingCount = 0;
        _f2Edge.Reset();
        _f3Edge.Reset();
    }
}
