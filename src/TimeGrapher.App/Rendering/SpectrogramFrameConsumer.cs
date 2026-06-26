using System.Diagnostics.CodeAnalysis;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class SpectrogramFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly SpectrogramRenderer _renderer;
    // The last analysis frame whose spectrogram update was processed. Dedup is by
    // frame identity, NOT buffer identity: the projector rotates a fixed buffer
    // pool, so a new publish can reuse an earlier PixelBuffer object — keying on
    // the buffer would skip that publish and freeze the column count. The active
    // tab also observes the same frame twice (router + RenderFrame); this skips
    // the second pass, and a re-routed kept frame cannot restore a theme-remapped
    // _latestSpectrogramImage.
    private AnalysisFrame? _lastObservedFrame;
    private PixelBuffer? _latestSpectrogramImage;
    private long _totalColumns;
    private double _latestColumnSeconds;
    private double _latestBeatPeriodS;
    // The most recent A-onset stream times (ascending, newest last), up to the
    // largest Compare Beats count: the renderer crops one lane per onset so each
    // beat stays centered regardless of the (non-integer) beat-period-in-columns.
    private readonly double[] _recentOnsetsS = new double[SpectrogramRenderer.MaxCompareBeats];
    private int _recentOnsetCount;
    private bool _displayedLight = PlotThemePalette.Current.IsLight;

    public SpectrogramFrameConsumer(SpectrogramRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.SpectrogramTabId;

    internal PixelBuffer? LatestSpectrogramImage => _latestSpectrogramImage;

    // The producer's monotonic source-column count from the latest observed
    // publish; handed to the renderer so a late tab switch keeps the full recent
    // window (it is absolute, so coalesced frames and buffer wraps never alias it).
    internal long TotalColumns => _totalColumns;

    // The recent onsets (ascending, newest last) handed to the renderer's per-lane
    // Beats crop; a snapshot copy for tests.
    internal double[] RecentOnsetsSnapshot() => _recentOnsetsS[.._recentOnsetCount];

    public void Initialize(AnalysisTabResetContext context)
    {
        _ = context;
        _renderer.InitializeLegend();
    }

    public void Reset(AnalysisTabResetContext context)
    {
        _ = context;
        _lastObservedFrame = null;
        _latestSpectrogramImage = null;
        _recentOnsetCount = 0;
        _totalColumns = 0;
        _renderer.Reset();
    }

    // Rebuild the legend for the new theme, then recolor the displayed image.
    // While running the worker recolors and republishes; but after a stop there
    // is no live worker (RunSessionController.SetSpectrogramColormap is a no-op
    // when idle), so mirror the kept image's colormap here. The remap writes
    // into a copy because the published buffer belongs to the worker's pool.
    public void ApplyTheme(PlotThemePalette theme)
    {
        _renderer.ApplyTheme(theme.IsLight);
        if (TryRemapKeptImage(theme.IsLight, out PixelBuffer? remapped))
        {
            _renderer.RenderWindowed(
                remapped, _totalColumns, _latestColumnSeconds, _latestBeatPeriodS, _recentOnsetsS.AsSpan(0, _recentOnsetCount));
        }
    }

    // Internal seam: the remap state machine is testable without the blit,
    // which needs the Avalonia platform.
    internal bool TryRemapKeptImage(bool light, [NotNullWhen(true)] out PixelBuffer? remapped)
    {
        bool changed = light != _displayedLight;
        _displayedLight = light;
        if (_latestSpectrogramImage == null || !changed)
        {
            remapped = null;
            return false;
        }

        remapped = MirrorColormap(_latestSpectrogramImage, light);
        _latestSpectrogramImage = remapped;
        return true;
    }

    internal static PixelBuffer MirrorColormap(PixelBuffer source, bool toLight)
    {
        var recolored = new PixelBuffer(source.Width, source.Height);
        Array.Copy(source.Pixels, recolored.Pixels, source.Pixels.Length);
        SpectrogramFrameProjector.MirrorColormap(recolored.Pixels, toLight);
        return recolored;
    }

    public void ObserveFrame(AnalysisFrame frame)
    {
        // Track the most recent A (beat) onsets: Last Beat phase-locks to the latest
        // and Compare Beats crops one lane per onset. Beat segments complete on their
        // own cadence, independent of the image publish, so this is read every frame
        // they are present.
        if (frame.BeatSegments != null && frame.BeatSegments.Segments.Count > 0)
        {
            var segments = frame.BeatSegments.Segments;
            int take = Math.Min(segments.Count, _recentOnsetsS.Length);
            _recentOnsetCount = take;
            for (int i = 0; i < take; i++)
            {
                BeatSegment seg = segments[segments.Count - take + i];
                _recentOnsetsS[i] = seg.StartTimeS + seg.AOffsetMs / 1000.0;
            }
        }

        // Process each delivered spectrogram publish once, keyed on the frame
        // object: a re-routed kept frame (or the active tab's second observe per
        // frame) is the same instance and must not re-run, while a genuinely new
        // publish that reuses a pooled buffer object is a different frame and must.
        if (frame.SpectrogramImageUpdated && frame.SpectrogramImage != null &&
            !ReferenceEquals(frame, _lastObservedFrame))
        {
            // The monotonic column count is the producer's absolute value (not a
            // delta), so a late tab switch, coalesced/dropped publishes, and the
            // 10 s source buffer wrapping never undercount it — the renderer crops
            // the correct recent window from it.
            _lastObservedFrame = frame;
            _latestSpectrogramImage = frame.SpectrogramImage;
            _totalColumns = frame.SpectrogramTotalColumns;
            _latestColumnSeconds = frame.SpectrogramColumnSeconds;
            _latestBeatPeriodS = frame.SpectrogramBeatPeriodS;
        }
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // The spectrogram is a Core-built pixel image (x = pixel columns of the
        // recent window, not stream time), so the review-cursor contract does
        // not apply; pause already freezes the image for inspection.
        _ = context;
        ObserveFrame(frame);
        if (_latestSpectrogramImage != null)
        {
            _renderer.RenderWindowed(
                _latestSpectrogramImage, _totalColumns, _latestColumnSeconds, _latestBeatPeriodS,
                _recentOnsetsS.AsSpan(0, _recentOnsetCount));
        }
    }
}
