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
/// cumulative BeatSegmentsSnapshot the frame carries (the Beat Noise's
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
/// each new snapshot version is deep-copied into a UI-owned cache (CopyForCache
/// -> _lastSnapshot); the lanes plus the delayed interaction state
/// (SelectPairAtPixelY, ghost overlay) read that cached copy, never a recycled
/// Core pool buffer.
/// </summary>
internal sealed class WaveformCompareRenderer
{
    /// <summary>
    /// Max-decimated points per lane (the strip-lane decimation policy):
    /// 12 lanes x 800 points stays inside the scope point budget.
    /// </summary>
    private const int LanePointBudget = 800;
    private const byte GhostAlpha = 120;
    private const byte SelectionFillAlpha = 35;

    /// <summary>X range: tic occupies the left half, toc the right half.</summary>
    private const double XMinMs = -2.0 * BeatSegmentCapture.PreEventMs;
    private const double XMaxMs = WaveformCompareLogic.TocXOffsetMs
        + WaveformCompareLogic.BeatDisplayWindowMs - BeatSegmentCapture.PreEventMs;
    /// <summary>Headroom above the top lane's normalized peak.</summary>
    private const double YHeadroom = 1.15;

    /// <summary>X margin (ms) applied to the mean C position for label placement.</summary>
    private const double LaneLabelXMarginMs = 10.0;

    /// <summary>Y offset from the lane baseline for label placement inside the lane.</summary>
    private const double LaneLabelYOffsetFromBaseline = 0.5;

    private readonly AvaPlot _plot;
    private readonly TextBlock _headerText;
    private readonly string _textFontFamily;

    private readonly List<double>[] _laneX;   // tic X per pair
    private readonly List<double>[] _laneY;   // tic Y per pair
    private readonly List<double>[] _tocX;    // toc X per pair
    private readonly List<double>[] _tocY;    // toc Y per pair

    private readonly Scatter?[] _laneScatters; // [pair*2]=tic, [pair*2+1]=toc
    private readonly Text?[] _laneLabels;      // [pair*2]=tic label, [pair*2+1]=toc label
    // A onset guides (one full-height vertical line per side)
    private VerticalLine? _aGuide;
    private Text? _aGuideLabel;
    private VerticalLine? _aGuideToc;
    private Text? _aGuideLabelToc;
    // Per-lane C peak markers (one line segment + one label per pair lane per side)
    private readonly LinePlot?[] _cGuidesTic;
    private readonly LinePlot?[] _cGuidesToc;
    private readonly Text?[] _cLabelsTic;
    private readonly Text?[] _cLabelsToc;
    private ReviewCursorLayer? _reviewCursor;

    // Ghost overlay: selected pair's waveform re-drawn at every other lane's baseline
    private readonly List<double>[] _ghostTicX;
    private readonly List<double>[] _ghostTicY;
    private readonly List<double>[] _ghostTocX;
    private readonly List<double>[] _ghostTocY;
    private readonly Scatter?[] _ghostTicScatters;
    private readonly Scatter?[] _ghostTocScatters;
    private VerticalSpan? _selectionSpan;
    private int _selectedPair = 0;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private ulong _lastHistoryVersion;
    private BeatSegmentsSnapshot? _lastSnapshot;
    // Toc x-offset (= the last rendered clipMs) so the review cursor shifts toc
    // segments into the right half exactly as the lanes/guides are drawn.
    private double _lastClipMs = WaveformCompareLogic.BeatDisplayWindowMs;
    // The segments actually drawn this render (a same-phase pair hides its older
    // duplicate). Mean-C guides/labels and the review cursor read this set, not the
    // raw snapshot, so they never reflect a beat that was not shown. Oldest-first.
    private IReadOnlyList<BeatSegment> _visibleSegments = Array.Empty<BeatSegment>();

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
        _cGuidesTic   = new LinePlot?[WaveformCompareLogic.PairLanes];
        _cGuidesToc   = new LinePlot?[WaveformCompareLogic.PairLanes];
        _cLabelsTic   = new Text?[WaveformCompareLogic.PairLanes];
        _cLabelsToc   = new Text?[WaveformCompareLogic.PairLanes];
        _ghostTicX       = new List<double>[WaveformCompareLogic.PairLanes];
        _ghostTicY       = new List<double>[WaveformCompareLogic.PairLanes];
        _ghostTocX       = new List<double>[WaveformCompareLogic.PairLanes];
        _ghostTocY       = new List<double>[WaveformCompareLogic.PairLanes];
        _ghostTicScatters = new Scatter?[WaveformCompareLogic.PairLanes];
        _ghostTocScatters = new Scatter?[WaveformCompareLogic.PairLanes];
        for (int i = 0; i < WaveformCompareLogic.PairLanes; i++)
        {
            _laneX[i] = new List<double>();
            _laneY[i] = new List<double>();
            _tocX[i]  = new List<double>();
            _tocY[i]  = new List<double>();
            _ghostTicX[i] = new List<double>();
            _ghostTicY[i] = new List<double>();
            _ghostTocX[i] = new List<double>();
            _ghostTocY[i] = new List<double>();
        }

        // Read-only comparison display: disable all built-in mouse interaction so
        // the lanes cannot be drag/wheel zoomed. Pair selection stays available —
        // it is wired through the control's pointer events, not this processor.
        _plot.UserInputProcessor.Disable();
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
        _visibleSegments = Array.Empty<BeatSegment>();
        _selectedPair = 0;
        _headerText.Text = WaveformCompareLogic.HeaderLine(null);

        Plot plot = _plot.Plot;
        plot.Clear();
        ApplyPlotTheme(plot);
        plot.YLabel("past \u2003\u2003\u2003\u2003\u2003\u2003\u2003\u2003\u25c4\u2003\u2003\u2003\u2003\u2003\u2003\u2003\u2003 current");
        plot.XLabel("(ms)");
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

        // Ghost overlay: selection span background + semi-transparent reference traces
        _selectionSpan = plot.Add.VerticalSpan(0.0, WaveformCompareLogic.LaneSpacing);
        _selectionSpan.IsVisible = false;
        _selectionSpan.LineStyle.IsVisible = false;
        for (int i = 0; i < WaveformCompareLogic.PairLanes; i++)
        {
            _ghostTicX[i].Clear();
            _ghostTicY[i].Clear();
            _ghostTocX[i].Clear();
            _ghostTocY[i].Clear();
            _ghostTicScatters[i] = plot.Add.Scatter(_ghostTicX[i], _ghostTicY[i]);
            _ghostTicScatters[i]!.LineWidth = 1;
            _ghostTicScatters[i]!.MarkerStyle.IsVisible = false;
            _ghostTicScatters[i]!.IsVisible = false;
            _ghostTocScatters[i] = plot.Add.Scatter(_ghostTocX[i], _ghostTocY[i]);
            _ghostTocScatters[i]!.LineWidth = 1;
            _ghostTocScatters[i]!.MarkerStyle.IsVisible = false;
            _ghostTocScatters[i]!.IsVisible = false;
        }

        // Legend: tic (green) left half, toc (red) right half — readable without axis text.
        if (_laneScatters[0] != null) _laneScatters[0]!.LegendText = "tic";
        if (_laneScatters[1] != null) _laneScatters[1]!.LegendText = "toc";
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.LowerRight;

        // A onset guides: one full-height vertical line per side.
        _aGuide         = AddGuide(plot);
        _aGuideLabel    = AddLabel(plot);
        _aGuideToc      = AddGuide(plot);
        _aGuideLabelToc = AddLabel(plot);

        // Per-lane C peak markers: line segments + labels updated each render.
        for (int i = 0; i < WaveformCompareLogic.PairLanes; i++)
        {
            _cGuidesTic[i] = AddCGuide(plot);
            _cGuidesToc[i] = AddCGuide(plot);
            _cLabelsTic[i] = AddLabel(plot);
            _cLabelsToc[i] = AddLabel(plot);
        }

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
        if (snapshot != null && (snapshot.Version != _lastVersion || _lastSnapshot == null))
        {
            // Deep-copy once per version: the cached snapshot is read later by the
            // interaction handlers (SelectPairAtPixelY / ghost overlay) long after the
            // BeatSegmentCapture pool (protected only two snapshot versions) recycles
            // the buffers. Gating on Version avoids re-copying every frame while the
            // same snapshot instance is reattached between segment completions.
            _lastSnapshot = CopyForCache(snapshot);
        }

        bool changed = false;
        if (snapshot != null && snapshot.Version != _lastVersion)
        {
            _lastVersion = snapshot.Version;
            RenderLanes(snapshot, frame.MetricsHistory);
            changed = true;
        }

        // Map the cursor after RenderLanes refreshes the visible-segment set so it
        // never points at a same-phase beat that was not drawn.
        changed |= UpdateReviewCursor(context.ReviewCursorTimeS);

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

    /// <summary>
    /// Deep-copies the pooled segment envelope/raw arrays into UI-owned storage so
    /// the cached snapshot (read later by SelectPairAtPixelY / ghost overlay on a
    /// click) never references a BeatSegmentCapture pool buffer that has since been
    /// recycled. Mirrors BeatNoiseScopeRenderer.CopyForCache; scalar fields, markers,
    /// and the average snapshot are immutable and shared as-is.
    /// </summary>
    private static BeatSegmentsSnapshot CopyForCache(BeatSegmentsSnapshot snapshot)
    {
        IReadOnlyList<BeatSegment> cached = snapshot.Segments;
        if (cached.Count == 0)
        {
            return snapshot;
        }

        var owned = new BeatSegment[cached.Count];
        for (int i = 0; i < cached.Count; i++)
        {
            BeatSegment s = cached[i];
            owned[i] = new BeatSegment
            {
                Samples = s.Samples.ToArray(),
                RawValid = s.RawValid,
                RawMin = s.RawMin.ToArray(),
                RawMax = s.RawMax.ToArray(),
                MsPerPoint = s.MsPerPoint,
                StartTimeS = s.StartTimeS,
                IsTic = s.IsTic,
                AOffsetMs = s.AOffsetMs,
                PeakValue = s.PeakValue,
                CPeakValid = s.CPeakValid,
                CPeakOffsetMs = s.CPeakOffsetMs,
                COnsetValid = s.COnsetValid,
                COnsetOffsetMs = s.COnsetOffsetMs,
            };
        }

        return new BeatSegmentsSnapshot
        {
            Version = snapshot.Version,
            Segments = owned,
            Markers = snapshot.Markers,
            LiftAngleDeg = snapshot.LiftAngleDeg,
            Average = snapshot.Average,
        };
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
        _lastClipMs = clipMs;

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
                if (_cGuidesTic[lane] != null) _cGuidesTic[lane]!.IsVisible = false;
                if (_cGuidesToc[lane] != null) _cGuidesToc[lane]!.IsVisible = false;
                if (_cLabelsTic[lane] != null) _cLabelsTic[lane]!.IsVisible = false;
                if (_cLabelsToc[lane] != null) _cLabelsToc[lane]!.IsVisible = false;
                continue;
            }

            // Assign each segment to its real half; a skipped beat can make the
            // pair the same phase, in which case one half is empty rather than a
            // beat drawn in the wrong half / mislabeled.
            (BeatSegment? ticSeg, BeatSegment? tocSeg) = WaveformCompareLogic.AssignPairHalves(
                segments[idxFirst], segments[idxLast]);

            double baseline = (WaveformCompareLogic.PairLanes - 1 - lane)
                              * WaveformCompareLogic.LaneSpacing;

            if (ticSeg is BeatSegment ticSegment)
            {
                FillLane(ticSegment, baseline, _laneX[lane], _laneY[lane], xOffset: 0.0, clipMs);
            }

            if (tocSeg is BeatSegment tocSegment)
            {
                FillLane(tocSegment, baseline, _tocX[lane], _tocY[lane], xOffset: clipMs, clipMs);
            }

            // Per-lane C peak markers at each beat's actual C-onset position.
            if (_cGuidesTic[lane] is LinePlot ticCGuide)
            {
                if (ticSeg is { CPeakValid: true } ticForC)
                {
                    double cX = ticForC.CPeakOffsetMs - ticForC.AOffsetMs;
                    ticCGuide.Line = new CoordinateLine(cX, baseline, cX, baseline + 1.0);
                    ticCGuide.IsVisible = true;
                    if (_cLabelsTic[lane] is Text ticCLabel)
                    {
                        ticCLabel.LabelText = "C";
                        ticCLabel.Location = new Coordinates(cX, baseline + 1.0);
                        ticCLabel.IsVisible = true;
                    }
                }
                else
                {
                    ticCGuide.IsVisible = false;
                    if (_cLabelsTic[lane] != null) _cLabelsTic[lane]!.IsVisible = false;
                }
            }

            if (_cGuidesToc[lane] is LinePlot tocCGuide)
            {
                if (tocSeg is { CPeakValid: true } tocForC)
                {
                    double cX = clipMs + (tocForC.CPeakOffsetMs - tocForC.AOffsetMs);
                    tocCGuide.Line = new CoordinateLine(cX, baseline, cX, baseline + 1.0);
                    tocCGuide.IsVisible = true;
                    if (_cLabelsToc[lane] is Text tocCLabel)
                    {
                        tocCLabel.LabelText = "C";
                        tocCLabel.Location = new Coordinates(cX, baseline + 1.0);
                        tocCLabel.IsVisible = true;
                    }
                }
                else
                {
                    tocCGuide.IsVisible = false;
                    if (_cLabelsToc[lane] != null) _cLabelsToc[lane]!.IsVisible = false;
                }
            }

            if (ticLabel != null)
            {
                ticLabel.IsVisible = ticSeg is BeatSegment;
                if (ticSeg is BeatSegment ticForLabel)
                {
                    ticLabel.LabelText = WaveformCompareLogic.LaneLabel(ticForLabel, history?.Bph ?? 0, snapshot.LiftAngleDeg);
                }
            }

            if (tocLabel != null)
            {
                tocLabel.IsVisible = tocSeg is BeatSegment;
                if (tocSeg is BeatSegment tocForLabel)
                {
                    tocLabel.LabelText = WaveformCompareLogic.LaneLabel(tocForLabel, history?.Bph ?? 0, snapshot.LiftAngleDeg);
                }
            }
        }

        // Mean-C guides/labels and the cursor use only the segments actually drawn
        // (a same-phase pair hides its older duplicate), so they never reflect a
        // beat that was not shown.
        _visibleSegments = WaveformCompareLogic.VisibleSegments(segments);

        // Position lane labels below each lane's mean C position with 10ms right margin
        double? ticMeanCOffsetMs = WaveformCompareLogic.MeanCPeakOffsetMs(_visibleSegments, ticOnly: true);
        double? tocMeanCOffsetMs = WaveformCompareLogic.MeanCPeakOffsetMs(_visibleSegments, ticOnly: false);

        for (int lane = 0; lane < pairCount; lane++)
        {
            double baseline = (WaveformCompareLogic.PairLanes - 1 - lane)
                              * WaveformCompareLogic.LaneSpacing;

            Text? ticLabel = _laneLabels[lane * 2];
            Text? tocLabel = _laneLabels[lane * 2 + 1];

            if (ticLabel != null && ticLabel.IsVisible)
            {
                // Position at mean C offset + X margin, inside the lane
                double ticLabelX = (ticMeanCOffsetMs ?? 0.0) + LaneLabelXMarginMs;
                ticLabel.Location = new Coordinates(ticLabelX, baseline + LaneLabelYOffsetFromBaseline);
            }

            if (tocLabel != null && tocLabel.IsVisible)
            {
                // Position at toc side: clipMs + mean C offset + X margin, inside the lane
                double tocLabelX = clipMs + (tocMeanCOffsetMs ?? 0.0) + LaneLabelXMarginMs;
                tocLabel.Location = new Coordinates(tocLabelX, baseline + LaneLabelYOffsetFromBaseline);
            }
        }

        UpdateGuides(_visibleSegments, pairCount, clipMs);
        UpdateGhostOverlay(snapshot, pairCount, clipMs);
        UpdateSelectionSpan(pairCount);
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

        // A onset guides: tic side at x=0, toc side at x=clipMs.
        SetGuide(_aGuide, _aGuideLabel,
            hasData ? 0.0 : null, WaveformCompareLogic.AGuideLabel, labelY);
        SetGuide(_aGuideToc, _aGuideLabelToc,
            hasData ? tocXMs : null, WaveformCompareLogic.AGuideLabel, labelY);


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
            _visibleSegments,
            _lastClipMs);
        return _reviewCursor.Update(offsetMs);
    }

    /// <summary>
    /// Selects the pair lane that contains the given pixel Y coordinate and
    /// re-draws the ghost overlay if the selection changes.
    /// </summary>
    public void SelectPairAtPixelY(double pixelY)
    {
        int pairCount = _lastSnapshot is { } s
            ? Math.Min(s.Segments.Count / 2, WaveformCompareLogic.PairLanes)
            : 0;
        Coordinates coords = _plot.Plot.GetCoordinates(0f, (float)pixelY);
        int pair = WaveformCompareLogic.PairFromDataY(coords.Y, pairCount);
        if (pair < 0 || pair == _selectedPair)
        {
            return;
        }

        _selectedPair = pair;
        if (_lastSnapshot is { } snapshot)
        {
            UpdateGhostOverlay(snapshot, pairCount, _lastClipMs);
            UpdateSelectionSpan(pairCount);
            _plot.Refresh();
        }
    }

    private void UpdateGhostOverlay(BeatSegmentsSnapshot snapshot, int pairCount, double clipMs)
    {
        IReadOnlyList<BeatSegment> segments = snapshot.Segments;

        // Resolve the selected pair's tic/toc segments.
        BeatSegment? selTic = null;
        BeatSegment? selToc = null;
        int selIdxLast  = segments.Count - 1 - _selectedPair * 2;
        int selIdxFirst = selIdxLast - 1;
        if (selIdxFirst >= 0 && _selectedPair < pairCount)
        {
            (selTic, selToc) = WaveformCompareLogic.AssignPairHalves(
                segments[selIdxFirst], segments[selIdxLast]);
        }

        for (int k = 0; k < WaveformCompareLogic.PairLanes; k++)
        {
            _ghostTicX[k].Clear();
            _ghostTicY[k].Clear();
            _ghostTocX[k].Clear();
            _ghostTocY[k].Clear();

            // No ghost on the selected lane itself, or when no source data.
            if (k == _selectedPair || (selTic == null && selToc == null) || k >= pairCount)
            {
                if (_ghostTicScatters[k] != null) _ghostTicScatters[k]!.IsVisible = false;
                if (_ghostTocScatters[k] != null) _ghostTocScatters[k]!.IsVisible = false;
                continue;
            }

            double kBaseline = (WaveformCompareLogic.PairLanes - 1 - k) * WaveformCompareLogic.LaneSpacing;

            if (selTic is BeatSegment ticSeg)
            {
                FillLane(ticSeg, kBaseline, _ghostTicX[k], _ghostTicY[k], xOffset: 0.0, clipMs);
            }

            if (selToc is BeatSegment tocSeg)
            {
                FillLane(tocSeg, kBaseline, _ghostTocX[k], _ghostTocY[k], xOffset: clipMs, clipMs);
            }

            if (_ghostTicScatters[k] != null)
            {
                _ghostTicScatters[k]!.IsVisible = _ghostTicX[k].Count > 0;
            }

            if (_ghostTocScatters[k] != null)
            {
                _ghostTocScatters[k]!.IsVisible = _ghostTocX[k].Count > 0;
            }
        }
    }

    private void UpdateSelectionSpan(int pairCount)
    {
        if (_selectionSpan == null || pairCount == 0)
        {
            if (_selectionSpan != null) _selectionSpan.IsVisible = false;
            return;
        }

        double baseline = (WaveformCompareLogic.PairLanes - 1 - _selectedPair) * WaveformCompareLogic.LaneSpacing;
        _selectionSpan.Y1 = baseline;
        _selectionSpan.Y2 = baseline + WaveformCompareLogic.LaneSpacing;
        _selectionSpan.IsVisible = true;
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
        label.LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
        label.Alignment = Alignment.UpperLeft;
        label.IsVisible = false;
        return label;
    }

    private static LinePlot AddCGuide(Plot plot)
    {
        LinePlot guide = plot.Add.Line(0.0, 0.0, 0.0, 1.0);
        guide.MarkerStyle.IsVisible = false;
        guide.LineWidth = 1;
        guide.LinePattern = LinePattern.Dashed;
        guide.IsVisible = false;
        return guide;
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

        // A and C guides: use the theme's primary text color (same as the lane
        // labels above) so they contrast against the scope background in both
        // themes. A hardcoded black turned invisible on the dark theme's black
        // ScopeBg; the palette color is dark in Light and light in Dark.
        Color guideColor = Color.FromARGB(_theme.TextPrimary);
        if (_aGuide != null)         _aGuide.LineColor           = guideColor;
        if (_aGuideLabel != null)    _aGuideLabel.LabelFontColor = guideColor;
        if (_aGuideToc != null)      _aGuideToc.LineColor        = guideColor;
        if (_aGuideLabelToc != null) _aGuideLabelToc.LabelFontColor = guideColor;
        foreach (LinePlot? cGuide in _cGuidesTic) if (cGuide != null) cGuide.LineColor = guideColor;
        foreach (LinePlot? cGuide in _cGuidesToc) if (cGuide != null) cGuide.LineColor = guideColor;
        foreach (Text? cLabel in _cLabelsTic) if (cLabel != null) cLabel.LabelFontColor = guideColor;
        foreach (Text? cLabel in _cLabelsToc) if (cLabel != null) cLabel.LabelFontColor = guideColor;
        Color ghostColor = Color.FromARGB(_theme.TraceGhost).WithAlpha(GhostAlpha);
        foreach (Scatter? s in _ghostTicScatters) if (s != null) s.LineColor = ghostColor;
        foreach (Scatter? s in _ghostTocScatters) if (s != null) s.LineColor = ghostColor;
        if (_selectionSpan != null)
        {
            _selectionSpan.FillStyle.Color = Color.FromARGB(_theme.TraceGhost).WithAlpha(SelectionFillAlpha);
        }

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
