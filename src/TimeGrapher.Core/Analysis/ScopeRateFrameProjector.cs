using System.Globalization;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

public sealed class ScopeRateFrameProjector
{
    // How much scope history the Core retains (and so how far back the user can
    // pan in the Rate/Scope tab). Decoupled from the on-screen zoom-out ceiling:
    // the renderer still caps any single view at 2 s (MaxScopeWindowSeconds), but
    // the buffer keeps 10 s so a pan can reveal earlier audio.
    private const int ScopeRetentionSeconds = 10;

    // Reference window for the decimation stride. The stride is sized so that a 2 s
    // span carries the full point budget, which keeps the on-screen resolution of
    // any (≤2 s) view constant regardless of how long the retention buffer is. The
    // retained point count then scales with ScopeRetentionSeconds, not the stride.
    private const int ScopeStrideReferenceSeconds = 2;

    // Batched front-trim threshold (see TrimScopeWindow). Expired front points are
    // physically removed only once this many have accumulated, amortising the
    // O(retained) RemoveRange shift over many passes. Sized to roughly one second of
    // decimated points at a typical 2 s point budget; the retained window therefore
    // stays bounded at ScopeRetentionSeconds plus at most one batch of carry-over.
    private const int ScopeTrimBatchPoints = 2048;

    // Per-publish copy window. Only the newest <see cref="ScopePublishWindowSeconds"/>
    // of decimated scope samples are copied onto each frame; the renderer merges
    // those slices into its own rolling history (so a pan/pause can still reach the
    // earlier ScopeRetentionSeconds). This bounds the steady-state snapshot copy to
    // the visible window instead of the full retention buffer — at stride 3 / 48 kHz
    // a 2 s slice is ~32 kpts versus ~160 kpts for the whole 10 s window. Sized to
    // the renderer's zoom-out ceiling (RateScopeRenderer.MaxScopeWindowSeconds) and
    // kept ≥ one publish interval of overlap so dropped frames leave no gap.
    private const double ScopePublishWindowSeconds = 2.0;

    /// <summary>
    /// Stream-time floor between scope-slice rebuilds (the SweepFrameProjector /
    /// MultiFilterFrameProjector throttle). The PCM/threshold slice changes every
    /// audio block (50-100 Hz) but the UI coalesces frames latest-wins, so rebuilding
    /// the ~32 kpt slice every pass is wasted copy; frames in between re-attach the
    /// same immutable slice. Markers and the rate series are independent (event-driven
    /// and already shared) and stay live per frame.
    /// </summary>
    public const double PublishIntervalS = 0.05;

    private readonly int _sampleRate;
    private readonly bool _useCOnset;
    private readonly int _scopeSnapshotPointBudget;
    private readonly List<double> _scopeWindowX = new();
    private readonly List<double> _scopeWindowPcm = new();
    private readonly List<double> _scopeWindowThreshold = new();
    private readonly List<ScopeVerticalMarker> _scopeWindowVerticalMarkers = new();
    private readonly List<ScopeHorizontalMarker> _scopeWindowHorizontalMarkers = new();
    private readonly List<ScopeTextMarker> _scopeWindowTextMarkers = new();
    // Latest tic/toc Error Rate series as immutable snapshots. Error Rate data changes only on
    // beat events (~8 Hz at 28800 BPH) while frames are produced per audio block
    // (50-100 Hz), so the snapshot is rebuilt once per actual update and the SAME
    // object is reattached to the frames in between — every frame still carries
    // the full latest bundle, without re-copying 250-point lists per frame.
    // Consumers only read GraphSeriesFrame (init-only, IReadOnlyList), so sharing
    // one instance across frames is safe.
    private GraphSeriesFrame? _latestTicRateSeries;
    private GraphSeriesFrame? _latestTocRateSeries;
    private string _latestResultsText = "";
    private ulong _localGraphTicks;
    private double _lastA;
    private bool _haveLastA;
    private bool _hasLatestResultsText;

    private int _strideScale = 1;

    // Scope-slice publish throttle (see PublishIntervalS). The cached slice is
    // re-attached to frames produced between rebuilds; GraphTickEnd is latched to
    // the slice's instant so the trace's right edge and the view advance together
    // (QAS-4 single-frame consistency) instead of the view racing ahead of a frozen
    // trace.
    private volatile int _publishIntervalScale = 1;
    private GraphSeriesFrame? _lastScopePcmSeries;
    private GraphSeriesFrame? _lastScopeThresholdSeries;
    private ulong _lastPublishedSample;
    private ulong _lastPublishedGraphTickEnd;
    private bool _hasPublishedScope;

    public ScopeRateFrameProjector(int sampleRate, bool useCOnset, int scopeSnapshotPointBudget)
    {
        _sampleRate = sampleRate;
        _useCOnset = useCOnset;
        _scopeSnapshotPointBudget = scopeSnapshotPointBudget;
    }

    /// <summary>
    /// Deadline-degradation knob: coarsen the scope decimation stride by an integer
    /// factor (1 = the configured point budget). Analysis thread only.
    /// </summary>
    public void SetScopeStrideScale(int scale)
    {
        _strideScale = Math.Max(1, scale);
    }

    /// <summary>
    /// Deadline-degradation knob: multiplies the scope-slice publish floor
    /// (1 = normal). The cheap knob to reach for before <see cref="SetScopeStrideScale"/>
    /// coarsens the stored resolution. Thread-safe; applied on the next AppendSnapshot.
    /// </summary>
    public void SetPublishIntervalScale(int scale)
    {
        _publishIntervalScale = Math.Max(1, scale);
    }

    public void Project(DetectorMetricsBlockUpdate update, AnalysisFrame frame)
    {
        DetectorResultSnapshot result = update.Result;
        frame.BeatSynced = result.SyncStatus == TgSyncStatus.Synced;
        double threshold = result.OnsetThreshold;
        ulong scopeStride = (ulong)ScopeSnapshotStride();
        ReadOnlySpan<float> processedPcm = result.ProcessedPcm.Span;
        for (int i = 0; i < result.ProcessedPcmLen; i++)
        {
            if ((_localGraphTicks % scopeStride) == 0)
            {
                _scopeWindowX.Add(_localGraphTicks);
                _scopeWindowPcm.Add(processedPcm[i]);
                _scopeWindowThreshold.Add(threshold);
            }
            _localGraphTicks++;
        }

        foreach (DetectedEventUpdate eventUpdate in update.DisplayEvents)
        {
            if (eventUpdate.Event.Type == TgEventType.A)
            {
                AppendAEventMarker(eventUpdate.EventSample, eventUpdate.Event.PeakValue);
            }
            else if (eventUpdate.Event.Type == TgEventType.C)
            {
                AppendCEventMarker(eventUpdate.Event, eventUpdate.EventSample);
            }
            else
            {
                Console.Error.WriteLine("Unknown Event Type");
            }
        }

        foreach (DetectedEventUpdate eventUpdate in update.MetricsEvents)
        {
            if (eventUpdate.Event.Type == TgEventType.C)
            {
                AppendCMetricText(eventUpdate.Event, eventUpdate.EventSample, eventUpdate.MetricsUpdate);
            }
            AppendMetricsUpdate(eventUpdate.MetricsUpdate, frame);
        }
    }

    public void AppendSnapshot(AnalysisFrame frame, bool force = false)
    {
        TrimScopeWindow();

        // force: the drain/flush path republishes regardless of the gate (the sibling
        // projectors' convention) so the final kept frame carries the freshest slice.
        ulong intervalSamples = (ulong)(PublishIntervalS * _sampleRate) * (ulong)_publishIntervalScale;
        if (force || !_hasPublishedScope || _localGraphTicks - _lastPublishedSample >= intervalSamples)
        {
            RebuildScopeSlice();
            _lastPublishedGraphTickEnd = _localGraphTicks;
            _lastPublishedSample = _localGraphTicks;
            _hasPublishedScope = true;
        }

        // Latch the view edge to the published slice so the renderer's live-follow
        // window does not run ahead of a throttled (frozen) trace.
        frame.GraphTickEnd = _lastPublishedGraphTickEnd;

        if (_lastScopePcmSeries != null)
        {
            frame.AddScopeSeries(_lastScopePcmSeries);
            frame.AddScopeSeries(_lastScopeThresholdSeries!);
        }

        // Markers are bounded by events over the retention window (~hundreds of
        // structs), not the per-sample PCM, so they ride every frame at the full
        // retained extent — the renderer needs them when a pan reaches back into the
        // history it has accumulated from earlier slices.
        frame.SetScopeMarkers(_scopeWindowVerticalMarkers, _scopeWindowHorizontalMarkers, _scopeWindowTextMarkers);

        if (_hasLatestResultsText)
        {
            frame.MetricsUpdate.SetResults(_latestResultsText);
        }

        if (_latestTicRateSeries != null)
        {
            frame.AddRateSeries(_latestTicRateSeries);
        }

        if (_latestTocRateSeries != null)
        {
            frame.AddRateSeries(_latestTocRateSeries);
        }
    }

    // Copies only the newest ScopePublishWindowSeconds of decimated scope samples
    // into fresh immutable series. The renderer merges these slices (keyed on the
    // absolute X ticks) into its own rolling history, so the earlier retention is
    // never re-copied per frame.
    private void RebuildScopeSlice()
    {
        int count = _scopeWindowX.Count;
        if (count == 0)
        {
            _lastScopePcmSeries = null;
            _lastScopeThresholdSeries = null;
            return;
        }

        double minX = 0.0;
        ulong windowSamples = (ulong)(ScopePublishWindowSeconds * _sampleRate);
        if (_localGraphTicks > windowSamples)
        {
            minX = _localGraphTicks - windowSamples;
        }

        int start = 0;
        while (start < count && _scopeWindowX[start] < minX)
        {
            start++;
        }

        int sliceLen = count - start;
        var sliceX = new List<double>(sliceLen);
        var slicePcm = new List<double>(sliceLen);
        var sliceThreshold = new List<double>(sliceLen);
        for (int i = start; i < count; i++)
        {
            sliceX.Add(_scopeWindowX[i]);
            slicePcm.Add(_scopeWindowPcm[i]);
            sliceThreshold.Add(_scopeWindowThreshold[i]);
        }

        _lastScopePcmSeries = new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.ScopePcm,
            X = sliceX,
            Y = slicePcm,
            Replace = true,
        };

        _lastScopeThresholdSeries = new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.ScopeThreshold,
            X = sliceX,
            Y = sliceThreshold,
            Replace = true,
        };
    }

    private void AppendAEventMarker(double eventSample, float peakValue)
    {
        _scopeWindowVerticalMarkers.Add(new ScopeVerticalMarker
        {
            X = eventSample,
            Height = peakValue,
            Color = Argb.Green,
        });

        if (_haveLastA)
        {
            double delta = eventSample - _lastA;
            _scopeWindowHorizontalMarkers.Add(new ScopeHorizontalMarker
            {
                Direction = HorizontalMarkerDirection.Outward,
                XLeft = _lastA,
                XRight = eventSample,
                Height = peakValue / 2.0,
                Color = Argb.Black,
            });

            _scopeWindowTextMarkers.Add(new ScopeTextMarker
            {
                X = _lastA + (delta / 2.0),
                Height = peakValue / 2.0,
                Text = " " + (delta * 1000.0 / _sampleRate).ToString("F2", CultureInfo.InvariantCulture) + " ms ",
                Color = Argb.Black,
                Alignment = MarkerTextAlignment.CenterTop,
            });
        }

        _lastA = eventSample;
        _haveLastA = true;
    }

    private void AppendCEventMarker(TgEvent ev, double eventSample)
    {
        if (_useCOnset && !ev.OnsetValid)
        {
            Console.Error.WriteLine("Invalid C Onset using C peak");
        }

        _scopeWindowVerticalMarkers.Add(new ScopeVerticalMarker
        {
            X = eventSample,
            Height = ev.PeakValue,
            Color = Argb.Red,
        });

        _scopeWindowHorizontalMarkers.Add(new ScopeHorizontalMarker
        {
            Direction = HorizontalMarkerDirection.Inward,
            XLeft = _lastA,
            XRight = eventSample,
            Length = InwardMarkerLength(_sampleRate),
            Height = ev.PeakValue,
            Color = Argb.Black,
        });
    }

    private void AppendCMetricText(TgEvent ev, double eventSample, WatchMetricsUpdate metricsUpdate)
    {
        _scopeWindowTextMarkers.Add(new ScopeTextMarker
        {
            X = eventSample + InwardMarkerLength(_sampleRate),
            Height = ev.PeakValue,
            Text = metricsUpdate.CMarkerText,
            Color = Argb.Black,
            Alignment = MarkerTextAlignment.LeftTop,
        });
    }

    private void AppendMetricsUpdate(WatchMetricsUpdate update, AnalysisFrame frame)
    {
        if (update.TicRateUpdated)
        {
            _latestTicRateSeries = new GraphSeriesFrame
            {
                Id = AnalysisGraphSeries.RateTic,
                X = new List<double>(update.XTic),
                Y = new List<double>(update.YTic),
                Replace = true,
            };
            frame.MetricsUpdate.SetTicRate(update.XTic, update.YTic);
        }
        if (update.TocRateUpdated)
        {
            _latestTocRateSeries = new GraphSeriesFrame
            {
                Id = AnalysisGraphSeries.RateToc,
                X = new List<double>(update.XToc),
                Y = new List<double>(update.YToc),
                Replace = true,
            };
            frame.MetricsUpdate.SetTocRate(update.XToc, update.YToc);
        }
        if (update.ResultsUpdated)
        {
            _latestResultsText = update.ResultsText;
            _hasLatestResultsText = true;
            frame.MetricsUpdate.SetResults(update.ResultsText);
        }
    }

    private int ScopeSnapshotStride()
    {
        int baseStride = Math.Max(1, _sampleRate / 48000);
        int maxWindowSamples = Math.Max(1, ScopeStrideReferenceSeconds * _sampleRate);
        int pointBudget = Math.Max(1, _scopeSnapshotPointBudget);
        int budgetStride = (int)Math.Ceiling(maxWindowSamples / (double)pointBudget);
        return Math.Max(baseStride, budgetStride) * _strideScale;
    }

    private void TrimScopeWindow()
    {
        double minX = 0.0;
        ulong historySamples = (ulong)(ScopeRetentionSeconds * _sampleRate);
        if (_localGraphTicks > historySamples)
        {
            minX = _localGraphTicks - historySamples;
        }

        int removeCount = 0;
        while (removeCount < _scopeWindowX.Count && _scopeWindowX[removeCount] < minX)
        {
            removeCount++;
        }
        // Batched/amortised front-trim: List.RemoveRange(0, n) shifts the whole
        // retained tail (O(retained)) every pass, so trimming the handful of points
        // that fall out of the 10 s retention each pass was a steady O(retained) cost.
        // RebuildScopeSlice re-scans the window and keeps only the newest 2 s, so any
        // expired-but-not-yet-removed points carried at the front are never published
        // — the scope output is identical whether they are dropped now or in a later
        // batch. Defer the shift until at least ScopeTrimBatchPoints have expired, so
        // the O(retained) shift is paid once per batch instead of once per pass.
        if (removeCount >= ScopeTrimBatchPoints)
        {
            _scopeWindowX.RemoveRange(0, removeCount);
            _scopeWindowPcm.RemoveRange(0, removeCount);
            _scopeWindowThreshold.RemoveRange(0, removeCount);
        }

        // Markers ride every frame at their full retained extent (SetScopeMarkers),
        // so they are trimmed per-pass to keep the published marker set exact. They
        // are bounded by events (~hundreds of structs), not per-sample PCM, so this
        // RemoveAll is not on the hot O(retained) path the batched trim above targets.
        _scopeWindowVerticalMarkers.RemoveAll(marker => marker.X < minX);
        _scopeWindowHorizontalMarkers.RemoveAll(marker =>
            Math.Max(marker.XLeft, marker.XRight) + marker.Length < minX);
        _scopeWindowTextMarkers.RemoveAll(marker => marker.X < minX);
    }

    private static double InwardMarkerLength(int sampleRate)
    {
        return 500.0 * (sampleRate / 48000.0);
    }
}
