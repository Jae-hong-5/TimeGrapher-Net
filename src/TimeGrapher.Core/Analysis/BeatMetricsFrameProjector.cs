using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Projects the per-event metrics updates of each analysis pass into the
/// cumulative <see cref="BeatMetricsHistory"/> and attaches its snapshot to the
/// outgoing frame. Sibling of <see cref="ScopeRateFrameProjector"/>; runs on the
/// analysis thread only.
/// </summary>
public sealed class BeatMetricsFrameProjector
{
    private readonly BeatMetricsHistory _history;

    /// <summary>
    /// The rate-error wrap scale and ring slot count come from the same
    /// <see cref="WatchMetrics"/> configuration the global ring uses, so the
    /// per-position tic/toc rate-error trace reproduces the global trace exactly.
    /// </summary>
    public BeatMetricsFrameProjector(double rateErrorYScale, int rateErrorRingCapacity)
    {
        _history = new BeatMetricsHistory(
            rateErrorYScale: rateErrorYScale,
            rateErrorRingCapacity: rateErrorRingCapacity);
    }

    // Written from any thread (UI position buttons), applied analysis-side at
    // the start of the next Project pass (the SweepFrameProjector knob pattern).
    private volatile int _requestedPosition = (int)WatchPosition.CH;
    private volatile bool _positionAggregateResetRequested;

    // Cached RateTic/RateToc wrappers. The history returns the same raw series
    // instance while the active ring is unchanged, so we rebuild the frame wrapper
    // only when that instance changes — every frame still carries the latest trace,
    // shared (not re-copied) in between.
    private GraphSeriesFrame? _ticSeries;
    private GraphSeriesFrame? _tocSeries;
    private MetricsHistorySeries? _ticSource;
    private MetricsHistorySeries? _tocSource;

    /// <summary>
    /// Requests the watch test position subsequent beats are tagged with.
    /// Thread-safe; applied on the analysis thread before the next events are
    /// recorded, so a beat is never tagged with a half-applied position.
    /// </summary>
    public void SetActivePosition(WatchPosition position)
    {
        _requestedPosition = (int)position;
    }

    /// <summary>
    /// Requests a multi-position sequence restart: the per-position aggregates
    /// clear on the analysis thread at the start of the next Project pass (the
    /// SetActivePosition knob flow), so the clear never races a beat being
    /// recorded. Thread-safe.
    /// </summary>
    public void ResetPositionAggregates()
    {
        _positionAggregateResetRequested = true;
    }

    public void Project(DetectorMetricsBlockUpdate update)
    {
        if (_positionAggregateResetRequested)
        {
            _positionAggregateResetRequested = false;
            _history.ResetPositionAggregates();
        }

        _history.SetActivePosition((WatchPosition)_requestedPosition);
        foreach (DetectedEventUpdate eventUpdate in update.MetricsEvents)
        {
            _history.Record(eventUpdate.MetricsUpdate);
        }
    }

    public void AppendSnapshot(AnalysisFrame frame)
    {
        frame.MetricsHistory = _history.CurrentSnapshot();

        // The Rate Scope (rate-error pane) and Beat Error tabs draw the active
        // position's tic/toc rate-error trace. Unlike the throttled history
        // snapshot, this is published on every frame so the traces stay as live as
        // they were when sourced from the global WatchMetrics ring (the
        // ScopeRateFrameProjector no longer emits these series). Both series are
        // always emitted as Replace series — an empty one clears the prior
        // position's trace when switching to a never-measured position.
        _history.CurrentActiveRateError(out MetricsHistorySeries tic, out MetricsHistorySeries toc);
        frame.AddRateSeries(WrapRateSeries(AnalysisGraphSeries.RateTic, tic, ref _ticSeries, ref _ticSource));
        frame.AddRateSeries(WrapRateSeries(AnalysisGraphSeries.RateToc, toc, ref _tocSeries, ref _tocSource));
    }

    private static GraphSeriesFrame WrapRateSeries(
        string id, MetricsHistorySeries source, ref GraphSeriesFrame? cached, ref MetricsHistorySeries? cachedSource)
    {
        if (cached is null || !ReferenceEquals(source, cachedSource))
        {
            cached = new GraphSeriesFrame { Id = id, X = source.X, Y = source.Y, Replace = true };
            cachedSource = source;
        }

        return cached;
    }
}
