using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Filter Scope projector: runs every raw input sample through the
/// <see cref="ScopeFilters"/> bank (F0..F3) and keeps a rolling
/// <see cref="WindowSeconds"/>-second window per view, decimated at the
/// producer to <see cref="FilterPointBudget"/> points per series (the
/// <see cref="ScopeRateFrameProjector"/> stride pattern), so per-frame
/// snapshot cost and memory stay bounded regardless of sample rate or run
/// length (SAP performance tactic: bound resource usage). Snapshots ride the
/// frame as four replace series sharing one X list. X values are absolute
/// raw-sample ticks on this projector's own counter — frame.GraphTickEnd
/// stays owned by ScopeRateFrameProjector; the renderer windows each plot to
/// its own series' max x. ProcessSamples/AppendSnapshot run on the analysis
/// thread only.
/// </summary>
public sealed class MultiFilterFrameProjector
{
    // Retained buffer and decimation budget. The renderer beat-locks to a small
    // window (a fraction of one beat period); peak-preserving (max-per-bin)
    // decimation keeps that window's envelope intact at a modest point count, so
    // the trace reads as a continuous filled burst without the Pi paying for a
    // full-resolution copy and render. ~6 kpts/s (stride 8 at 48 kHz).
    public const int WindowSeconds = 1;
    public const int FilterPointBudget = 6000;

    // Batched front-trim threshold (see TrimWindow). Expired front points are tracked
    // logically every pass but physically removed (an O(retained) List.RemoveRange
    // shift) only once this many have accumulated, amortising the shift over many
    // passes. ~0.5 s of decimated points at the FilterPointBudget cadence; the
    // retained window stays bounded at WindowSeconds plus at most one batch of
    // carry-over.
    private const int TrimBatchPoints = 3000;

    /// <summary>
    /// Stream-time floor between series rebuilds. The five rebuilt lists
    /// (~240 KB) used to be allocated per analysis pass — at the Pi's 192 kHz
    /// pass cadence (~94/s) that was megabytes per second of analysis-thread
    /// churn, most of it discarded by the latest-wins UI coalescer. Frames in
    /// between re-attach the same immutable series instances (the rate-series
    /// sharing pattern). The floor gates every capture path: at 48 kHz it
    /// lowers the rebuild cadence on Windows WaveInEvent (20 ms periods, so
    /// the gate fires every 3rd pass) and on PipeWire f32 (42.7 ms reads)
    /// alike; only the ALSA S16 path (~85 ms passes) already exceeded it.
    /// </summary>
    public const double PublishIntervalS = 0.05;

    private static readonly string[] SeriesIds =
    {
        AnalysisGraphSeries.FilterF0,
        AnalysisGraphSeries.FilterF1,
        AnalysisGraphSeries.FilterF2,
        AnalysisGraphSeries.FilterF3,
    };

    private readonly int _sampleRate;
    private readonly ScopeFilters _filters;
    private readonly List<double> _windowX = new();
    private readonly List<double>[] _windowY;
    private ulong _sampleTicks;

    // Logical front offset of expired points not yet physically removed (see
    // TrimWindow). Publishing copies from this offset, so the published window is
    // identical to the per-pass-trimmed window while the costly RemoveRange shift is
    // batched.
    private int _trimStart;

    // Peak-preserving (max-per-bin) decimation accumulator: the running max of
    // each filter over the current stride-bin, and the bin's start tick. Storing
    // each bin's peak (not a subsample) keeps the envelope, so a sparse trace
    // still reads as a continuous filled burst — and the point count stays low
    // enough for the Raspberry Pi. The filter outputs are all non-negative, so
    // the max alone is the upper envelope (the mirror gives the lower).
    private readonly double[] _binMax;
    private ulong _binStartTick;

    // Deadline-degradation knob (analysis thread applies it; written from the
    // worker's ladder): stretches the publish floor under sustained pressure.
    private volatile int _publishIntervalScale = 1;
    private GraphSeriesFrame[]? _lastSeries;
    private ulong _lastPublishedTick;

    public MultiFilterFrameProjector(int sampleRate)
    {
        _sampleRate = sampleRate;
        _filters = new ScopeFilters(sampleRate);
        _windowY = new List<double>[SeriesIds.Length];
        for (int i = 0; i < _windowY.Length; i++)
        {
            _windowY[i] = new List<double>();
        }

        _binMax = new double[SeriesIds.Length];
    }

    /// <summary>
    /// Feeds one raw audio block (the same span AnalysisWorker hands the
    /// detector pipeline). The filter bank consumes every sample to keep its
    /// state exact; the peak (max) of each stride-bin is stored for display, so
    /// the decimated trace preserves the envelope instead of aliasing it.
    /// </summary>
    public void ProcessSamples(ReadOnlySpan<float> block)
    {
        ulong stride = (ulong)SnapshotStride();
        for (int i = 0; i < block.Length; i++)
        {
            ScopeFilterSample sample = _filters.Process(block[i]);

            // Accumulate the per-filter max over the current stride-bin; emit the
            // peak (with the bin's start tick) when the bin completes. A bin may
            // span blocks — the accumulator persists across calls.
            ulong binPos = _sampleTicks % stride;
            if (binPos == 0)
            {
                _binStartTick = _sampleTicks;
                _binMax[0] = sample.F0;
                _binMax[1] = sample.F1;
                _binMax[2] = sample.F2;
                _binMax[3] = sample.F3;
            }
            else
            {
                if (sample.F0 > _binMax[0]) _binMax[0] = sample.F0;
                if (sample.F1 > _binMax[1]) _binMax[1] = sample.F1;
                if (sample.F2 > _binMax[2]) _binMax[2] = sample.F2;
                if (sample.F3 > _binMax[3]) _binMax[3] = sample.F3;
            }

            if (binPos == stride - 1)
            {
                _windowX.Add(_binStartTick);
                _windowY[0].Add(_binMax[0]);
                _windowY[1].Add(_binMax[1]);
                _windowY[2].Add(_binMax[2]);
                _windowY[3].Add(_binMax[3]);
            }

            _sampleTicks++;
        }
    }

    /// <summary>
    /// Deadline-degradation knob: multiplies the publish floor (1 = normal).
    /// Thread-safe; applied on the next AppendSnapshot.
    /// </summary>
    public void SetPublishIntervalScale(int scale)
    {
        _publishIntervalScale = Math.Max(1, scale);
    }

    public void AppendSnapshot(AnalysisFrame frame, bool force = false)
    {
        TrimWindow();
        // Live window length excludes the expired front points carried before the
        // next batched physical trim (see TrimWindow); publishing reads from
        // _trimStart so the result is identical to a per-pass-trimmed window.
        int liveCount = _windowX.Count - _trimStart;
        if (liveCount == 0)
        {
            return;
        }

        // force: the drain/flush path republishes regardless of the gate (the
        // SoundPrintFrameProjector convention), so the final kept frame always
        // carries the freshest filter window.
        ulong intervalSamples = (ulong)(PublishIntervalS * _sampleRate) * (ulong)_publishIntervalScale;
        if (force || _lastSeries == null || _sampleTicks - _lastPublishedTick >= intervalSamples)
        {
            var x = new List<double>(liveCount);
            x.AddRange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_windowX).Slice(_trimStart, liveCount));
            var series = new GraphSeriesFrame[SeriesIds.Length];
            for (int i = 0; i < SeriesIds.Length; i++)
            {
                var y = new List<double>(liveCount);
                y.AddRange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_windowY[i]).Slice(_trimStart, liveCount));
                series[i] = new GraphSeriesFrame
                {
                    Id = SeriesIds[i],
                    X = x,
                    Y = y,
                    Replace = true,
                };
            }

            _lastSeries = series;
            _lastPublishedTick = _sampleTicks;
        }

        foreach (GraphSeriesFrame series in _lastSeries)
        {
            frame.AddScopeSeries(series);
        }
    }

    private int SnapshotStride()
    {
        int baseStride = Math.Max(1, _sampleRate / 48000);
        int maxWindowSamples = Math.Max(1, WindowSeconds * _sampleRate);
        int budgetStride = (int)Math.Ceiling(maxWindowSamples / (double)FilterPointBudget);
        return Math.Max(baseStride, budgetStride);
    }

    private void TrimWindow()
    {
        double minX = 0.0;
        ulong historySamples = (ulong)(WindowSeconds * _sampleRate);
        if (_sampleTicks > historySamples)
        {
            minX = _sampleTicks - historySamples;
        }

        // Count expired front points from the current logical start. List.RemoveRange
        // (0, n) shifts the whole retained tail (O(retained)) every call, so removing
        // the few points that fall out of the window each pass was a steady
        // O(retained) cost. Track the expired front logically (_trimStart) and publish
        // from it, so the published window is unchanged; only pay the physical shift
        // once at least TrimBatchPoints have expired.
        int removeCount = _trimStart;
        while (removeCount < _windowX.Count && _windowX[removeCount] < minX)
        {
            removeCount++;
        }

        if (removeCount - _trimStart >= TrimBatchPoints)
        {
            _windowX.RemoveRange(0, removeCount);
            for (int i = 0; i < _windowY.Length; i++)
            {
                _windowY[i].RemoveRange(0, removeCount);
            }
            _trimStart = 0;
        }
        else
        {
            _trimStart = removeCount;
        }
    }
}
