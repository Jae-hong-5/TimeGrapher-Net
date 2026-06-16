using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Scope 2 averaging: accumulates fixed 20 ms beat-noise traces into two lanes
/// assigned by the alternating beat phase. The lanes deliberately carry no
/// tic/toc meaning (the plan: the system does not guarantee which physical
/// noise lands on which axis, so the UI labels them trace 1/2).
///
/// With Σ enabled, each lane averages up to <see cref="IntervalsPerLane"/>
/// intervals and the cycle freezes once both lanes are full (50 tic + 50 tac);
/// intermediate counts are exposed so progress can show. With Σ disabled, each
/// lane just holds its newest single trace. Accumulators are two fixed
/// double[<see cref="LanePoints"/>] arrays, so memory is bounded regardless of
/// run length. Analysis thread only (driven by <see cref="BeatSegmentCapture"/>).
/// </summary>
public sealed class BeatNoiseAverager
{
    /// <summary>Fixed Scope 2 lane window (ms).</summary>
    public const double LaneWindowMs = 20.0;

    /// <summary>Fixed decimated points per lane trace (0.025 ms/point).</summary>
    public const int LanePoints = 800;

    public const double MsPerPoint = LaneWindowMs / LanePoints;

    /// <summary>Intervals per lane after which the averaging cycle completes.</summary>
    public const int IntervalsPerLane = 50;

    private static readonly int[] MilestoneCounts = { 10, 20, 30, 40, 50 };

    private readonly double[][] _sums = { new double[LanePoints], new double[LanePoints] };
    private readonly int[] _counts = new int[2];
    private readonly double[] _peakSums = new double[2];
    private readonly BeatNoiseAverageMilestone?[] _milestones = new BeatNoiseAverageMilestone?[5];
    private bool _sigmaEnabled;

    public bool SigmaEnabled => _sigmaEnabled;

    /// <summary>True once both lanes hold their full interval count (Σ on only).</summary>
    public bool Frozen =>
        _sigmaEnabled && _counts[0] >= IntervalsPerLane && _counts[1] >= IntervalsPerLane;

    /// <summary>
    /// Turns Σ averaging on/off. A change resets both lanes (a half-averaged
    /// cycle must not blend into the new mode). Returns true when state changed.
    /// </summary>
    public bool SetSigmaEnabled(bool enabled)
    {
        if (_sigmaEnabled == enabled)
        {
            return false;
        }

        _sigmaEnabled = enabled;
        Reset();
        return true;
    }

    public void Reset()
    {
        Array.Clear(_sums[0]);
        Array.Clear(_sums[1]);
        _counts[0] = 0;
        _counts[1] = 0;
        _peakSums[0] = 0.0;
        _peakSums[1] = 0.0;
        Array.Clear(_milestones);
    }

    /// <summary>
    /// Adds one 20 ms interval trace to the lane of the given phase. Returns
    /// true when the lane changed (caller marks its snapshot dirty).
    /// </summary>
    public bool Add(bool firstLane, ReadOnlySpan<float> trace)
    {
        if (trace.Length != LanePoints)
        {
            throw new ArgumentException($"Lane traces must have {LanePoints} points.", nameof(trace));
        }

        int lane = firstLane ? 0 : 1;
        if (_sigmaEnabled && _counts[lane] >= IntervalsPerLane)
        {
            // This lane's half of the cycle is complete; ignore further beats.
            return false;
        }

        double[] sum = _sums[lane];
        if (!_sigmaEnabled && _counts[lane] > 0)
        {
            // Σ off: the lane shows only its newest trace.
            Array.Clear(sum);
            _counts[lane] = 0;
            _peakSums[lane] = 0.0;
        }

        float peak = 0f;
        for (int i = 0; i < LanePoints; i++)
        {
            sum[i] += trace[i];
            if (trace[i] > peak)
            {
                peak = trace[i];
            }
        }

        _counts[lane]++;
        _peakSums[lane] += peak;
        CaptureMilestone(Math.Min(_counts[0], _counts[1]));
        return true;
    }

    /// <summary>
    /// Builds an immutable snapshot of both lane averages. Called only when a
    /// lane changed (per beat at most), so the trace copies stay off the
    /// per-block hot path.
    /// </summary>
    public BeatNoiseAverageSnapshot Snapshot() => new()
    {
        SigmaEnabled = _sigmaEnabled,
        Frozen = Frozen,
        IntervalsPerLane = IntervalsPerLane,
        MsPerPoint = MsPerPoint,
        Lane1Count = _counts[0],
        Lane2Count = _counts[1],
        Lane1 = LaneAverage(0),
        Lane2 = LaneAverage(1),
        Lane1MeanPeak = _counts[0] > 0 ? _peakSums[0] / _counts[0] : 0.0,
        Lane2MeanPeak = _counts[1] > 0 ? _peakSums[1] / _counts[1] : 0.0,
        Milestones = MilestoneSnapshots(),
    };

    private void CaptureMilestone(int count)
    {
        if (!_sigmaEnabled)
        {
            return;
        }

        int index = Array.IndexOf(MilestoneCounts, count);
        if (index < 0)
        {
            return;
        }

        _milestones[index] = new BeatNoiseAverageMilestone
        {
            IntervalCount = count,
            Lane1 = LaneAverage(0),
            Lane2 = LaneAverage(1),
        };
    }

    private IReadOnlyList<BeatNoiseAverageMilestone> MilestoneSnapshots()
    {
        var milestones = new List<BeatNoiseAverageMilestone>();
        for (int i = 0; i < _milestones.Length; i++)
        {
            if (_milestones[i] is { } milestone)
            {
                milestones.Add(milestone);
            }
        }

        return milestones;
    }

    private IReadOnlyList<float> LaneAverage(int lane)
    {
        int count = _counts[lane];
        if (count == 0)
        {
            return Array.Empty<float>();
        }

        double[] sum = _sums[lane];
        var average = new float[LanePoints];
        for (int i = 0; i < LanePoints; i++)
        {
            average[i] = (float)(sum[i] / count);
        }

        return average;
    }
}
