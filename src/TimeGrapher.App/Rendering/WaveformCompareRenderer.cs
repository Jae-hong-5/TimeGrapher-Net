using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Waveform Comparison Display with Timing Markers, rendered from the
/// cumulative BeatSegmentsSnapshot the frame carries (the Beat-Noise Scope's
/// segment infrastructure reused for cross-beat comparison).
///
/// One plot stacks the recent beat pairs: each pair-lane displays the tic beat
/// on the left half (x = 0..BeatDisplayWindowMs) and the toc beat on the right
/// half (x = TocXOffsetMs..TocXOffsetMs+BeatDisplayWindowMs), all A-aligned
/// within their respective half. Newest pair at the top. Pooled vertical guides
/// mark x = 0 and x = TocXOffsetMs (A onset for tic/toc sides) and the
/// per-side mean C-peak interval. The header line above reads the current rate /
/// beat error / BPH from the cumulative metrics history.
///
/// All plottables refill in place; the lanes re-render only when the segments
/// snapshot version changes and the header only when the metrics-history
/// version changes, so coalesced or repeated frames cost nothing. Segments
/// reference pooled Core buffers that stay valid only until rotated out, so
/// every render re-reads from the latest snapshot and nothing UI-side caches
/// sample data beyond it.
/// </summary>
internal sealed class WaveformCompareRenderer
{
    /// <summary>
    /// Max-decimated points per lane (the strip-lane decimation policy):
    /// 12 lanes x 800 points stays inside the scope point budget.
    /// </summary>
    private const int LanePointBudget = 800;

    /// <summary>X range: tic occupies the left half, toc the right half.</summary>
    private const double XMinMs = -2.0 * BeatSegmentCapture.PreEventMs;
    private const double XMaxMs = WaveformCompareLogic.TocXOffsetMs
        + WaveformCompareLogic.BeatDisplayWindowMs - BeatSegmentCapture.PreEventMs;
    /// <summary>Approximate x position for peak in each beat half (ms from A).</summary>
    private const double PeakOffsetMs = 50.0;
    /// <summary>X offset from peak for the label placement.</summary>
    private const double LaneLabelPeakOffsetMs = 10.0;

    /// <summary>Lane label top relative to the lane baseline (traces peak at +1.0).</summary>
    private const double LaneLabelYOffset = 1.12;

    /// <summary>Headroom above the top lane's normalized peak.</summary>
    private const double YHeadroom = 1.15;

    private readonly AvaPlot _plot;
    private readonly TextBlock _headerText;
    private readonly string _textFontFamily;

    private readonly List<double>[] _laneX;   // tic X per pair
    private readonly List<double>[] _laneY;   // tic Y per pair
    private readonly List<double>[] _tocX;    // toc X per pair
    private readonly List<double>[] _tocY;    // toc Y per pair

    private readonly Scatter?[] _laneScatters; // [pair*2]=tic, [pair*2+1]=toc
    private readonly Text?[] _laneLabels;      // [pair*2]=tic label, [pair*2+1]=toc label
    // Tic-side guides (at x = 0 and mean-C for the tic segment)
    private VerticalLine? _aGuide;
    private VerticalLine? _cMeanGuide;
    private Text? _aGuideLabel;
    private Text? _cMeanGuideLabel;
    // Toc-side guides (offset by TocXOffsetMs)
    private VerticalLine? _aGuideToc;
    private VerticalLine? _cMeanGuideToc;
    private Text? _aGuideLabelToc;
    private Text? _cMeanGuideLabelToc;
    private ReviewCursorLayer? _reviewCursor;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private ulong _lastHistoryVersion;
    private BeatSegmentsSnapshot? _lastSnapshot;

    public WaveformCompareRenderer(AvaPlot plot, TextBlock headerText, string textFontFamily)
    {
        _plot = plot;
        _headerText = headerText;
        _textFontFamily = textFontFamily;

        _laneX = new List<double>[WaveformCompareLogic.PairLanes];
        _laneY = new List<double>[WaveformCompareLogic.PairLanes];
        _tocX  = new List<double>[WaveformCompareLogic.PairLanes];
        _tocY  = new List<double>[WaveformCompareLogic.PairLanes];
        _laneScatters = new Scatter?[WaveformCompareLogic.PairLanes * 2];
        _laneLabels   = new Text?[WaveformCompareLogic.PairLanes * 2];
        for (int i = 0; i < WaveformCompareLogic.PairLanes; i++)
        {
            _laneX[i] = new List<double>();
            _laneY[i] = new List<double>();
            _tocX[i]  = new List<double>();
            _tocY[i]  = new List<double>();
        }
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_plot.Plot);
        ApplySeriesTheme();
        _plot.Refresh();
    }

    public void CreateGraphs()
    {
        _lastVersion = 0;
        _lastHistoryVersion = 0;
        _lastSnapshot = null;
        _headerText.Text = WaveformCompareLogic.HeaderLine(null);

        Plot plot = _plot.Plot;
        plot.Clear();
        ApplyPlotTheme(plot);
        plot.YLabel("Pairs (newest at the top)");
        plot.XLabel("tic \u2190 | \u2192 toc \n (ms)");
        plot.Axes.Left.TickLabelStyle.IsVisible = false;

        for (int i = 0; i < WaveformCompareLogic.PairLanes; i++)
        {
            _laneX[i].Clear();
            _laneY[i].Clear();
            _tocX[i].Clear();
            _tocY[i].Clear();
            _laneScatters[i * 2] = plot.Add.Scatter(_laneX[i], _laneY[i]);
            _laneScatters[i * 2]!.LineWidth = 1;
            _laneScatters[i * 2]!.MarkerStyle.IsVisible = false;
            _laneLabels[i * 2] = AddLabel(plot);
            _laneScatters[i * 2 + 1] = plot.Add.Scatter(_tocX[i], _tocY[i]);
            _laneScatters[i * 2 + 1]!.LineWidth = 1;
            _laneScatters[i * 2 + 1]!.MarkerStyle.IsVisible = false;
            _laneLabels[i * 2 + 1] = AddLabel(plot);
        }

        _aGuide = AddGuide(plot);
        _cMeanGuide = AddGuide(plot);
        _aGuideLabel = AddLabel(plot);
        _cMeanGuideLabel = AddLabel(plot);
        _aGuideToc = AddGuide(plot);
        _cMeanGuideToc = AddGuide(plot);
        _aGuideLabelToc = AddLabel(plot);
        _cMeanGuideLabelToc = AddLabel(plot);
        _reviewCursor = AddCursor(plot);

        plot.Axes.SetLimitsX(XMinMs, XMaxMs);
        plot.Axes.SetLimitsY(-0.1, YTop(0));
        // Floor the left edge at the configured pre-A strip (XMinMs), not 0:
        // this axis is "ms from A", so the negative pre-roll region carries the
        // lane labels and pre-event envelope and must stay reachable.
        PlotAxisRules.ClampLeftEdge(plot, XMinMs);

        ApplySeriesTheme();
        _plot.Refresh();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        UpdateHeader(frame.MetricsHistory);

        BeatSegmentsSnapshot? snapshot = frame.BeatSegments;
        if (snapshot != null)
        {
            _lastSnapshot = snapshot;
        }

        bool changed = UpdateReviewCursor(context.ReviewCursorTimeS);

        if (snapshot != null && snapshot.Version != _lastVersion)
        {
            _lastVersion = snapshot.Version;
            RenderLanes(snapshot, frame.MetricsHistory);
            changed = true;
        }

        if (changed)
        {
            _plot.Refresh();
        }
    }

    /// <summary>
    /// Header numeric line, re-formatted only when the history version changes
    /// (the header lives outside the plot, so it never forces a plot refresh).
    /// </summary>
    private void UpdateHeader(BeatMetricsHistorySnapshot? history)
    {
        if (history == null || history.Version == _lastHistoryVersion)
        {
            return;
        }

        _lastHistoryVersion = history.Version;
        _headerText.Text = WaveformCompareLogic.HeaderLine(history);
    }

    private void RenderLanes(BeatSegmentsSnapshot snapshot, BeatMetricsHistorySnapshot? history)
    {
        IReadOnlyList<BeatSegment> segments = snapshot.Segments;
        int pairCount = Math.Min(segments.Count / 2, WaveformCompareLogic.PairLanes);

        // Clip each half at min(BeatDisplayWindowMs, beatPeriodMs) so a fast
        // escapement (short beat period) never bleeds a second beat into one lane.
        double beatPeriodMs = history?.Bph > 0
            ? 3600000.0 / history.Bph
            : WaveformCompareLogic.BeatDisplayWindowMs;
        double clipMs = Math.Min(WaveformCompareLogic.BeatDisplayWindowMs, beatPeriodMs);
        double xMaxMs = 2 * clipMs;

        for (int lane = 0; lane < WaveformCompareLogic.PairLanes; lane++)
        {
            // Newest pair at lane 0 (top): last two segments = pair 0.
            int idxLast  = segments.Count - 1 - lane * 2;
            int idxFirst = idxLast - 1;
            bool hasPair = idxFirst >= 0;

            _laneX[lane].Clear();
            _laneY[lane].Clear();
            _tocX[lane].Clear();
            _tocY[lane].Clear();

            Text? ticLabel = _laneLabels[lane * 2];
            Text? tocLabel = _laneLabels[lane * 2 + 1];

            if (!hasPair)
            {
                if (ticLabel != null) ticLabel.IsVisible = false;
                if (tocLabel != null) tocLabel.IsVisible = false;
                continue;
            }

            BeatSegment segA = segments[idxFirst];
            BeatSegment segB = segments[idxLast];
            BeatSegment ticSeg = segA.IsTic ? segA : segB;
            BeatSegment tocSeg = segA.IsTic ? segB : segA;

            double baseline = (WaveformCompareLogic.PairLanes - 1 - lane)
                              * WaveformCompareLogic.LaneSpacing;

            FillLane(ticSeg, baseline, _laneX[lane], _laneY[lane], xOffset: 0.0, clipMs);
            FillLane(tocSeg, baseline, _tocX[lane],  _tocY[lane],
                     xOffset: clipMs, clipMs);

            if (ticLabel != null)
            {
                ticLabel.IsVisible = true;
                ticLabel.LabelText = WaveformCompareLogic.LaneLabel(ticSeg);
                ticLabel.Location = new Coordinates(PeakOffsetMs + LaneLabelPeakOffsetMs, baseline + LaneLabelYOffset);
            }

            if (tocLabel != null)
            {
                tocLabel.IsVisible = true;
                tocLabel.LabelText = WaveformCompareLogic.LaneLabel(tocSeg);
                tocLabel.Location = new Coordinates(
                    clipMs + PeakOffsetMs + LaneLabelPeakOffsetMs,
                    baseline + LaneLabelYOffset);
            }
        }

        // Position lane labels below each lane's mean C position with 10ms right margin
        double? ticMeanCOffsetMs = WaveformCompareLogic.MeanCPeakOffsetMs(segments, ticOnly: true);
        double? tocMeanCOffsetMs = WaveformCompareLogic.MeanCPeakOffsetMs(segments, ticOnly: false);

        for (int lane = 0; lane < pairCount; lane++)
        {
            double baseline = (WaveformCompareLogic.PairLanes - 1 - lane)
                              * WaveformCompareLogic.LaneSpacing;

            Text? ticLabel = _laneLabels[lane * 2];
            Text? tocLabel = _laneLabels[lane * 2 + 1];

            if (ticLabel != null && ticLabel.IsVisible)
            {
                // Position at mean C offset + 10ms right margin, below this lane's baseline
                double ticLabelX = (ticMeanCOffsetMs ?? 0.0) + 10.0;
                ticLabel.Location = new Coordinates(ticLabelX, baseline - 0.25);
            }

            if (tocLabel != null && tocLabel.IsVisible)
            {
                // Position at toc side: clipMs + mean C offset + 10ms right margin, below this lane's baseline
                double tocLabelX = clipMs + (tocMeanCOffsetMs ?? 0.0) + 10.0;
                tocLabel.Location = new Coordinates(tocLabelX, baseline - 0.25);
            }
        }

        UpdateGuides(segments, pairCount, clipMs);
        _plot.Plot.Axes.SetLimitsX(XMinMs, xMaxMs);
        _plot.Plot.Axes.SetLimitsY(-0.1, YTop(pairCount));
    }

    /// <summary>
    /// Fills one lane with the segment's envelope, A-aligned, with an optional
    /// x offset (used to shift the toc trace into the right half of the plot).
    /// Points beyond <see cref="WaveformCompareLogic.BeatDisplayWindowMs"/> after
    /// the A event are skipped so each half shows only its own beat.
    /// </summary>
    private static void FillLane(BeatSegment segment, double baseline,
        List<double> x, List<double> y, double xOffset, double clipMs)
    {
        EnvelopeLaneSampler.MaxDecimateNormalized(
            segment.Samples.Span, LanePointBudget,
            (p, _, stride, normalized) =>
            {
                double relX = p * stride * segment.MsPerPoint - segment.AOffsetMs;
                if (relX > clipMs)
                {
                    return;
                }

                x.Add(xOffset + relX);
                y.Add(baseline + normalized);
            });
    }

    private void UpdateGuides(IReadOnlyList<BeatSegment> segments, int pairCount, double tocXMs)
    {
        double labelY = YTop(pairCount);
        bool hasData = pairCount > 0;

        // Tic-side: A at x=0, mean-C from tic segments
        SetGuide(_aGuide, _aGuideLabel,
            hasData ? 0.0 : null, WaveformCompareLogic.AGuideLabel, labelY);
        double? ticMeanC = WaveformCompareLogic.MeanCPeakOffsetMs(segments, ticOnly: true);
        SetGuide(_cMeanGuide, _cMeanGuideLabel, ticMeanC,
            ticMeanC is double tm ? WaveformCompareLogic.CMeanGuideLabel(tm) : "", labelY);

        // Toc-side: A and mean-C shifted right by tocXMs (= clipMs, dynamic)
        SetGuide(_aGuideToc, _aGuideLabelToc,
            hasData ? tocXMs : null,
            WaveformCompareLogic.AGuideLabel, labelY);
        double? tocMeanC = WaveformCompareLogic.MeanCPeakOffsetMs(segments, ticOnly: false);
        SetGuide(_cMeanGuideToc, _cMeanGuideLabelToc,
            tocMeanC.HasValue ? tocXMs + tocMeanC.Value : null,
            tocMeanC is double tocm ? WaveformCompareLogic.CMeanGuideLabel(tocm) : "", labelY);
    }

    private static double YTop(int pairCount) =>
        (Math.Max(1, pairCount) - 1) * WaveformCompareLogic.LaneSpacing + YHeadroom;

    /// <summary>Review-cursor contract: a dotted marker at the scrub time's A-relative offset.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        if (_reviewCursor == null)
        {
            return false;
        }

        double? offsetMs = WaveformCompareLogic.CursorOffsetMs(
            reviewCursorTimeS,
            _lastSnapshot?.Segments ?? Array.Empty<BeatSegment>());
        return _reviewCursor.Update(offsetMs);
    }

    private static VerticalLine AddGuide(Plot plot)
    {
        VerticalLine guide = plot.Add.VerticalLine(0.0);
        guide.LineWidth = 1;
        guide.LinePattern = LinePattern.Dashed;
        guide.IsVisible = false;
        guide.EnableAutoscale = false;
        return guide;
    }

    private Text AddLabel(Plot plot)
    {
        Text label = plot.Add.Text("", 0.0, 0.0);
        label.LabelFontName = _textFontFamily;
        label.LabelFontSize = 11;
        label.Alignment = Alignment.UpperLeft;
        label.IsVisible = false;
        return label;
    }

    private static void SetGuide(VerticalLine? guide, Text? label, double? x, string text, double labelY)
    {
        if (guide == null || label == null)
        {
            return;
        }

        bool visible = x is not null;
        guide.IsVisible = visible;
        label.IsVisible = visible;
        if (x is double position)
        {
            guide.X = position;
            label.LabelText = text;
            label.Location = new Coordinates(position, labelY);
        }
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private void ApplySeriesTheme()
    {
        for (int i = 0; i < _laneScatters.Length; i++)
        {
            Scatter? scatter = _laneScatters[i];
            if (scatter != null)
            {
                // Even index = tic (TraceTick color), odd index = toc (TraceTock color)
                scatter.LineColor = (i % 2 == 0)
                    ? Color.FromARGB(_theme.TraceTick)
                    : Color.FromARGB(_theme.TraceTock);
            }
        }

        foreach (Text? label in _laneLabels)
        {
            if (label != null)
            {
                label.LabelFontColor = Color.FromARGB(_theme.TextPrimary);
            }
        }

        // A = tick green, C = tock red: the same themed event color mapping the
        // scope markers use (RateScopeRenderer.ThemeColor).
        if (_aGuide != null)             _aGuide.LineColor              = Color.FromARGB(_theme.TraceTick);
        if (_aGuideLabel != null)        _aGuideLabel.LabelFontColor    = Color.FromARGB(_theme.TraceTick);
        if (_cMeanGuide != null)         _cMeanGuide.LineColor          = Color.FromARGB(_theme.TraceTock);
        if (_cMeanGuideLabel != null)    _cMeanGuideLabel.LabelFontColor = Color.FromARGB(_theme.TraceTock);
        if (_aGuideToc != null)          _aGuideToc.LineColor           = Color.FromARGB(_theme.TraceTick);
        if (_aGuideLabelToc != null)     _aGuideLabelToc.LabelFontColor  = Color.FromARGB(_theme.TraceTick);
        if (_cMeanGuideToc != null)      _cMeanGuideToc.LineColor       = Color.FromARGB(_theme.TraceTock);
        if (_cMeanGuideLabelToc != null) _cMeanGuideLabelToc.LabelFontColor = Color.FromARGB(_theme.TraceTock);

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
