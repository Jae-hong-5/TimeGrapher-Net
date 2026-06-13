namespace TimeGrapher.Core.Shared;

/// <summary>
/// One captured beat-noise segment: the decimated envelope window around a
/// detected A event (a short pre-roll before A through the configured window
/// length), with the in-window marker offsets the Beat-Noise Scope displays.
/// <para>
/// <see cref="Samples"/> references a pooled buffer owned by
/// <see cref="TimeGrapher.Core.Analysis.BeatSegmentCapture"/> (the
/// SoundPrintFrameProjector publish-pool pattern): the contents are immutable
/// by contract until enough newer segments have completed to rotate the pool,
/// so consumers must read only the segments of the latest snapshot and never
/// cache a segment across snapshots.
/// </para>
/// </summary>
public sealed class BeatSegment
{
    /// <summary>Decimated envelope of the window (rectified, so values are non-negative).</summary>
    public ReadOnlyMemory<float> Samples { get; init; }

    /// <summary>
    /// True when the un-rectified raw-waveform window (<see cref="RawMin"/> /
    /// <see cref="RawMax"/>) was captured for this segment. False when the
    /// producer fed only the envelope (e.g. the detection-accuracy harness),
    /// in which case consumers must fall back to <see cref="Samples"/>.
    /// </summary>
    public bool RawValid { get; init; }

    /// <summary>
    /// Per-point minimum of the un-rectified raw input over the same window and
    /// point grid as <see cref="Samples"/>. Together with <see cref="RawMax"/>
    /// this is min/max decimation of the real (bipolar) watch signal — what the
    /// escapement views show instead of the negated envelope. Empty unless
    /// <see cref="RawValid"/>.
    /// </summary>
    public ReadOnlyMemory<float> RawMin { get; init; }

    /// <summary>Per-point maximum of the un-rectified raw input; see <see cref="RawMin"/>.</summary>
    public ReadOnlyMemory<float> RawMax { get; init; }

    /// <summary>Milliseconds covered by one sample point.</summary>
    public double MsPerPoint { get; init; }

    /// <summary>Stream time (s) of the window start (A minus the pre-roll).</summary>
    public double StartTimeS { get; init; }

    /// <summary>
    /// Alternating beat phase (the odd-beat-number "tic" convention of
    /// <see cref="BeatTimingSample.IsTic"/>) — a lane assignment, not a claim
    /// about which physical tick/tock noise this is.
    /// </summary>
    public bool IsTic { get; init; }

    /// <summary>A (unlock) event offset within the window (ms).</summary>
    public double AOffsetMs { get; init; }

    /// <summary>Envelope peak value of the A event.</summary>
    public float PeakValue { get; init; }

    /// <summary>C (drop/lock) peak offset within the window (ms); valid only when the C arrived inside the window.</summary>
    public bool CPeakValid { get; init; }
    public double CPeakOffsetMs { get; init; }

    /// <summary>C onset offset within the window (ms); valid only when the detector located the C cluster's rising edge.</summary>
    public bool COnsetValid { get; init; }
    public double COnsetOffsetMs { get; init; }
}

/// <summary>
/// Scope 2 averaging state: two fixed 20 ms beat-noise traces accumulated by
/// alternating beat phase. The lanes are deliberately numbered 1/2, not
/// labeled tic/toc — the system does not guarantee which physical noise lands
/// on which lane. Traces are immutable copies built per lane update (per beat
/// at most), shared across frames with the surrounding snapshot.
/// </summary>
public sealed class BeatNoiseAverageSnapshot
{
    public static readonly BeatNoiseAverageSnapshot Empty = new();

    /// <summary>Whether Σ averaging is on (off = each lane holds its newest single trace).</summary>
    public bool SigmaEnabled { get; init; }

    /// <summary>True once both lanes hold their full interval count (cycle complete).</summary>
    public bool Frozen { get; init; }

    /// <summary>Intervals per lane that complete the averaging cycle.</summary>
    public int IntervalsPerLane { get; init; }

    /// <summary>Milliseconds covered by one lane trace point.</summary>
    public double MsPerPoint { get; init; }

    /// <summary>Intervals accumulated into each lane so far (progress display).</summary>
    public int Lane1Count { get; init; }
    public int Lane2Count { get; init; }

    /// <summary>Per-lane averaged 20 ms traces (empty until the lane's first interval).</summary>
    public IReadOnlyList<float> Lane1 { get; init; } = Array.Empty<float>();
    public IReadOnlyList<float> Lane2 { get; init; } = Array.Empty<float>();

    /// <summary>Mean of the per-interval envelope peaks of each lane (the plan's per-axis average amplitude).</summary>
    public double Lane1MeanPeak { get; init; }
    public double Lane2MeanPeak { get; init; }
}

/// <summary>
/// Ring of the most recent completed beat segments, carried by every frame.
/// Cumulative by design: the render scheduler coalesces frames latest-wins, so
/// dropped intermediate frames lose nothing. Rebuilt only when a segment
/// completes; in between, frames share the same immutable instance (the
/// BeatMetricsHistorySnapshot sharing pattern).
/// </summary>
public sealed class BeatSegmentsSnapshot
{
    /// <summary>Increments whenever snapshot content changed; consumers can skip re-rendering on equal versions.</summary>
    public ulong Version { get; init; }

    /// <summary>Completed segments, oldest first (bounded by the capture's segment ring).</summary>
    public IReadOnlyList<BeatSegment> Segments { get; init; } = Array.Empty<BeatSegment>();

    /// <summary>Lift angle (deg) the producing analysis run was configured with.</summary>
    public double LiftAngleDeg { get; init; }

    /// <summary>Scope 2 lane-averaging state of the same capture.</summary>
    public BeatNoiseAverageSnapshot Average { get; init; } = BeatNoiseAverageSnapshot.Empty;
}
