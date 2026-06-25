using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Escapement Analyzer and Marker-Line Display, rendered from the cumulative
/// BeatSegmentsSnapshot the frame carries (the Beat Noise's segment
/// infrastructure reused for fine-grained intra-beat timing).
///
/// One large plot shows a full oscillation — the tic beat and the toc beat that
/// follows it — as the real raw waveform: the un-rectified bipolar signal as
/// captured, drawn as the per-point min/max outlines so the vertically-symmetric
/// scope look is the actual data, not the negated envelope (it falls back to the
/// rectified envelope only when the producer fed no raw). Both beats come from
/// the tic segment's own window, which is long enough (400 ms) to span the toc
/// that follows ~half a beat later, so the two noise groups sit at their true
/// spacing on one continuous trace. Over it sit pooled vertical timing markers
/// and millisecond labels for each beat's escapement-cycle events: A (green
/// dashed), C peak (red dashed) and C onset (red dotted, only when the detector
/// located the cluster's rising edge). The numeric panel below reads the latest
/// beat's A→C interval per reference, the onset-vs-peak delta, and — via
/// EscapementTimingTracker fed here on each snapshot-version change — the
/// windowed mean±sigma of both references plus which reference is more
/// repeatable.
///
/// The displayed pair is the latest COMPLETE tic→toc pair, always anchored on
/// the tic phase, so the pattern stays "tic-toc, tic-toc" frame after frame
/// rather than flipping to "toc-tic" whenever the newest beat happens to be a
/// tic. (Selecting "the latest two beats" would alternate the lead phase every
/// beat.)
///
/// The X axis is milliseconds relative to the tic's A (tic A at 0, matching the
/// reference figure's zeroed axis) and is zoomed to frame both noise groups with
/// a margin on each side (a fraction of the content width) so the tic and toc
/// bursts sit inset from the frame edges rather than jammed against them — not
/// the full 400 ms capture window. Y scales to the visible bursts only, so
/// anything past the toc never inflates it.
///
/// All plottables refill in place; re-renders only when the snapshot version
/// changes, so coalesced or repeated frames cost nothing. Segments reference
/// pooled Core buffers that stay valid only until rotated out, so every render
/// re-reads from the latest snapshot and nothing UI-side caches sample data
/// beyond it.
/// </summary>
internal sealed class EscapementAnalyzerRenderer
{
    private const double YHeadroom = 1.1;
    /// <summary>Top label row (A and C peak), as a fraction of the envelope max.</summary>
    private const double TopLabelFraction = 1.06;
    /// <summary>Second label row (C onset, which sits close to C peak), kept below the top row.</summary>
    private const double SecondLabelFraction = 0.97;

    /// <summary>
    /// Each side's view margin as a fraction of the content width (the tic's
    /// pre-A roll start through the last event), so the tic and toc bursts sit
    /// inset from the frame edges instead of jammed against them.
    /// </summary>
    private const double ViewPadFraction = 0.12;
    /// <summary>Smallest per-side margin (ms): floors the fractional pad so a tight single beat keeps room for its labels.</summary>
    private const double ViewPadMs = 8.0;
    /// <summary>Smallest visible span (ms), so a very tight single-beat A→C still frames sensibly.</summary>
    private const double MinViewSpanMs = 18.0;

    // Marker slots: the tic group (A, C peak, C onset) then the toc group.
    private const int MarkerCount = 6;
    private const int MarkerTicA = 0;
    private const int MarkerTicCPeak = 1;
    private const int MarkerTicCOnset = 2;
    private const int MarkerTocA = 3;
    private const int MarkerTocCPeak = 4;
    private const int MarkerTocCOnset = 5;

    private readonly AvaPlot _plot;
    private readonly TextBlock[] _valueTexts;
    private readonly string _textFontFamily;
    private readonly EscapementTimingTracker _tracker = new();

    private readonly List<double> _envelopeX = new();
    private readonly List<double> _envelopeY = new();
    // Lower outline of the raw bipolar waveform (RawMin); _envelopeY carries the
    // upper outline (RawMax) when raw is available, the rectified envelope otherwise.
    private readonly List<double> _rawMinY = new();

    private Scatter? _envelopeScatter;
    private Scatter? _rawMinScatter;
    private readonly VerticalLine?[] _markers = new VerticalLine?[MarkerCount];
    private readonly Text?[] _labels = new Text?[MarkerCount];
    private Text? _signalQualityLabel;
    private readonly SignalQualityOverlayState _signalQualityOverlay = new();

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private ulong _lastObservedVersion;
    private double _lastViewRight = MinViewSpanMs - BeatSegmentCapture.PreEventMs;
    private double _lastViewTop = 1.0;

    private static bool IsAMarker(int index) => index == MarkerTicA || index == MarkerTocA;

    public EscapementAnalyzerRenderer(AvaPlot plot, TextBlock[] valueTexts, string textFontFamily)
    {
        _plot = plot;
        _valueTexts = valueTexts;
        _textFontFamily = textFontFamily;

        // Read-only escapement-zoomed view: the X/Y limits are recomputed and
        // re-applied on every new beat, so a mouse zoom only snaps back next
        // frame. Disable the built-in mouse interaction so the view stays at its
        // computed zoom instead of fighting the user.
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
        _lastObservedVersion = 0;
        _tracker.Reset();
        foreach (TextBlock value in _valueTexts)
        {
            value.Text = VarioReadout.Missing;
        }

        Plot plot = _plot.Plot;
        plot.Clear();
        _envelopeX.Clear();
        _envelopeY.Clear();
        _rawMinY.Clear();
        ApplyPlotTheme(plot);
        plot.YLabel("Signal Level");
        plot.XLabel("ms from A");
        _envelopeScatter = plot.Add.Scatter(_envelopeX, _envelopeY);
        _envelopeScatter.LineWidth = 1;
        _envelopeScatter.MarkerStyle.IsVisible = false;
        _rawMinScatter = plot.Add.Scatter(_envelopeX, _rawMinY);
        _rawMinScatter.LineWidth = 1;
        _rawMinScatter.MarkerStyle.IsVisible = false;
        _rawMinScatter.IsVisible = false;
        // Two beats' worth of markers: A and C peak dashed, C onset dotted.
        for (int i = 0; i < MarkerCount; i++)
        {
            bool onset = i == MarkerTicCOnset || i == MarkerTocCOnset;
            _markers[i] = AddMarker(plot, onset ? LinePattern.Dotted : LinePattern.Dashed);
            _labels[i] = AddLabel(plot);
        }
        _signalQualityLabel = AddSignalQualityLabel(plot);
        _signalQualityOverlay.Reset();
        // A-relative, escapement-zoomed view: A at 0 with the capture's pre-A
        // roll showing as negative time. The window start is exactly -AOffsetMs
        // (the segment opens AOffsetMs before A, capped at PreEventMs), so floor
        // the left edge there rather than at 0 or pan/zoom-out would hide the roll.
        plot.Axes.SetLimitsX(-BeatSegmentCapture.PreEventMs, MinViewSpanMs - BeatSegmentCapture.PreEventMs);
        PlotAxisRules.ClampLeftEdge(plot, -BeatSegmentCapture.PreEventMs);

        ApplySeriesTheme();
        _plot.Refresh();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    /// <summary>
    /// Feeds the repeatability tracker from every routed frame (the consumer's
    /// ObserveFrame path), so the advertised last-32-beats window keeps
    /// accumulating while another tab is active. Version-gated and O(ring) per
    /// new snapshot (~2/s), so the observe path stays trivial.
    /// </summary>
    public void ObserveSegments(AnalysisFrame frame)
    {
        BeatSegmentsSnapshot? snapshot = frame.BeatSegments;
        if (snapshot == null || snapshot.Version == _lastObservedVersion)
        {
            return;
        }

        _lastObservedVersion = snapshot.Version;
        _tracker.Accumulate(snapshot);
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // Review cursor deliberately not rendered here: this is a tic→toc
        // inspection view whose x-domain is milliseconds relative to the tic's
        // A, not stream time, so context.ReviewCursorTimeS has no meaningful x
        // mapping on this plot.
        _ = context;

        BeatSegmentsSnapshot? snapshot = frame.BeatSegments;
        if (snapshot == null || snapshot.Version == _lastVersion)
        {
            return;
        }

        _lastVersion = snapshot.Version;
        // Catch-up for re-routed frames (tab switch / scrub) whose snapshot the
        // observe path already consumed - Accumulate is watermark-idempotent.
        ObserveSegments(frame);

        (BeatSegment? tic, BeatSegment? toc) = SelectPair(snapshot);
        BeatSegment? latest = snapshot.Segments.Count > 0 ? snapshot.Segments[^1] : null;
        double labelExtent = RenderPair(tic, toc);
        UpdateMarkers(tic, toc, labelExtent);
        SetSignalQuality(latest?.Quality ?? SignalQualityFlags.None);
        UpdateReadout(latest);
        _plot.Refresh();
    }

    /// <summary>
    /// The latest COMPLETE tic→toc pair, oldest-of-pair first: the newest toc
    /// segment together with the tic immediately before it. Anchoring on the tic
    /// keeps the displayed order stable ("tic-toc, tic-toc") rather than letting
    /// the lead phase alternate with whichever beat is newest. Falls back to the
    /// latest single segment when no adjacent tic→toc pair exists yet (the first
    /// beat, or a missed beat broke the alternation); toc is null then.
    /// </summary>
    private static (BeatSegment? Tic, BeatSegment? Toc) SelectPair(BeatSegmentsSnapshot snapshot)
    {
        IReadOnlyList<BeatSegment> segments = snapshot.Segments;
        for (int j = segments.Count - 1; j >= 1; j--)
        {
            if (!segments[j].IsTic && segments[j - 1].IsTic)
            {
                return (segments[j - 1], segments[j]);
            }
        }

        return (segments.Count > 0 ? segments[^1] : null, null);
    }

    /// <summary>
    /// Offset (ms) added to the toc segment's own in-window event offsets to place
    /// them on the tic-A-relative axis: the real stream-time gap between the two
    /// window starts, minus the tic's A offset (which defines axis zero). Zero
    /// when there is no toc (single-beat fallback).
    /// </summary>
    private static double TocBaseMs(BeatSegment tic, BeatSegment? toc) =>
        toc == null ? 0.0 : (toc.StartTimeS - tic.StartTimeS) * 1000.0 - tic.AOffsetMs;

    /// <summary>
    /// Refills the trace series (the tic anchor's window, which spans the toc too)
    /// and rescales the axes; returns the signal level the marker labels sit
    /// above. Draws the real raw bipolar waveform (RawMin/Max outlines, symmetric
    /// about zero) when the anchor carries raw, and falls back to the rectified
    /// envelope otherwise. X is re-zeroed at the tic's A and the view is zoomed to
    /// frame both beats (see <see cref="ComputeView"/>).
    /// </summary>
    private double RenderPair(BeatSegment? tic, BeatSegment? toc)
    {
        _envelopeX.Clear();
        _envelopeY.Clear();
        _rawMinY.Clear();
        if (tic == null)
        {
            if (_rawMinScatter != null)
            {
                _rawMinScatter.IsVisible = false;
            }

            _lastViewRight = MinViewSpanMs - BeatSegmentCapture.PreEventMs;
            _lastViewTop = 1.0;
            return 1.0;
        }

        (double startRel, double endRel, double visibleEndAbsMs) = ComputeView(tic, toc, TocBaseMs(tic, toc));
        // The view now pads left of the capture's pre-A roll, so lower the
        // pan/zoom-out floor to that padded edge — CreateGraphs floored it at
        // -PreEventMs, which would otherwise clip the new left margin away.
        PlotAxisRules.ClampLeftEdge(_plot.Plot, startRel);
        return tic.RawValid
            ? RenderRaw(tic, startRel, endRel, visibleEndAbsMs)
            : RenderEnvelope(tic, startRel, endRel, visibleEndAbsMs);
    }

    /// <summary>
    /// Tic-A-relative, zoomed X view: tic A sits at 0 and the content runs from
    /// the anchor's pre-A roll (-AOffsetMs) through the last event — the toc's C
    /// (or A, when its C is missing) when a toc is shown, otherwise the tic's own
    /// C. Both ends are padded by <see cref="ViewPadFraction"/> of that content
    /// width (floored at <see cref="ViewPadMs"/> so labels still fit on a tight
    /// single beat) so the bursts sit inset from the frame edges, and the whole
    /// span is floored at <see cref="MinViewSpanMs"/>. Returns the view edges in
    /// tic-A-relative ms and the absolute-ms cutoff (into the anchor window) past
    /// which points are off-screen, so Y can scale to the visible bursts only.
    /// </summary>
    private static (double StartRel, double EndRel, double VisibleEndAbsMs) ComputeView(
        BeatSegment tic, BeatSegment? toc, double tocBaseMs)
    {
        double aMs = tic.AOffsetMs;
        double lastEventRel = 0.0;
        if (tic.CPeakValid)
        {
            lastEventRel = Math.Max(lastEventRel, tic.CPeakOffsetMs - aMs);
        }

        if (tic.COnsetValid)
        {
            lastEventRel = Math.Max(lastEventRel, tic.COnsetOffsetMs - aMs);
        }

        if (toc != null)
        {
            // The toc's A is always shown; its C extends the frame when present.
            lastEventRel = Math.Max(lastEventRel, tocBaseMs + toc.AOffsetMs);
            if (toc.CPeakValid)
            {
                lastEventRel = Math.Max(lastEventRel, tocBaseMs + toc.CPeakOffsetMs);
            }

            if (toc.COnsetValid)
            {
                lastEventRel = Math.Max(lastEventRel, tocBaseMs + toc.COnsetOffsetMs);
            }
        }

        double contentStart = -aMs;
        double pad = Math.Max((lastEventRel - contentStart) * ViewPadFraction, ViewPadMs);
        double startRel = contentStart - pad;
        double endRel = lastEventRel + pad;
        double shortfall = MinViewSpanMs - (endRel - startRel);
        if (shortfall > 0.0)
        {
            startRel -= shortfall / 2.0;
            endRel += shortfall / 2.0;
        }

        return (startRel, endRel, endRel + aMs);
    }

    /// <summary>Raw bipolar waveform: max outline up, min outline down, symmetric Y.</summary>
    private double RenderRaw(BeatSegment segment, double startRel, double endRel, double visibleEndAbsMs)
    {
        double aMs = segment.AOffsetMs;
        ReadOnlySpan<float> min = segment.RawMin.Span;
        ReadOnlySpan<float> max = segment.RawMax.Span;
        int count = Math.Min(min.Length, max.Length);
        double extent = 0.0;
        for (int i = 0; i < count; i++)
        {
            double absMs = i * segment.MsPerPoint;
            _envelopeX.Add(absMs - aMs);
            _envelopeY.Add(max[i]);
            _rawMinY.Add(min[i]);
            if (absMs > visibleEndAbsMs)
            {
                continue;
            }

            double pointExtent = Math.Max(Math.Abs(min[i]), Math.Abs(max[i]));
            if (pointExtent > extent)
            {
                extent = pointExtent;
            }
        }

        if (extent <= 0.0)
        {
            extent = 1.0;
        }

        if (_rawMinScatter != null)
        {
            _rawMinScatter.IsVisible = true;
        }

        _lastViewRight = endRel;
        _lastViewTop = YHeadroom * extent;
        _plot.Plot.Axes.SetLimitsX(startRel, endRel);
        _plot.Plot.Axes.SetLimitsY(-_lastViewTop, _lastViewTop);
        return extent;
    }

    /// <summary>Fallback rectified envelope (no raw fed); returns the envelope max.</summary>
    private double RenderEnvelope(BeatSegment segment, double startRel, double endRel, double visibleEndAbsMs)
    {
        if (_rawMinScatter != null)
        {
            _rawMinScatter.IsVisible = false;
        }

        double aMs = segment.AOffsetMs;
        ReadOnlySpan<float> samples = segment.Samples.Span;
        double max = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            double absMs = i * segment.MsPerPoint;
            _envelopeX.Add(absMs - aMs);
            _envelopeY.Add(samples[i]);
            if (absMs <= visibleEndAbsMs && samples[i] > max)
            {
                max = samples[i];
            }
        }

        if (max <= 0.0)
        {
            max = 1.0;
        }

        _lastViewRight = endRel;
        _lastViewTop = YHeadroom * max;
        _plot.Plot.Axes.SetLimitsX(startRel, endRel);
        _plot.Plot.Axes.SetLimitsY(-0.02 * max, _lastViewTop);
        return max;
    }

    /// <summary>
    /// Places both beats' A / C peak / C onset markers on the tic-A-relative axis.
    /// The tic group is measured against its own A (tic A at 0); the toc group is
    /// shifted by <see cref="TocBaseMs"/> to its real position, while each C label
    /// still reports that beat's own A→C interval.
    /// </summary>
    private void UpdateMarkers(BeatSegment? tic, BeatSegment? toc, double traceExtent)
    {
        if (tic == null)
        {
            for (int i = 0; i < MarkerCount; i++)
            {
                SetMarker(i, null, "");
            }

            return;
        }

        double topLabelY = TopLabelFraction * traceExtent;
        double secondLabelY = SecondLabelFraction * traceExtent;

        double ticAMs = tic.AOffsetMs;
        SetMarker(MarkerTicA, 0.0, EscapementReadout.AMarkerLabel, topLabelY);
        SetMarker(
            MarkerTicCPeak,
            tic.CPeakValid ? tic.CPeakOffsetMs - ticAMs : null,
            EscapementReadout.CPeakMarkerLabel(tic.CPeakOffsetMs - ticAMs),
            topLabelY);
        SetMarker(
            MarkerTicCOnset,
            tic.COnsetValid ? tic.COnsetOffsetMs - ticAMs : null,
            EscapementReadout.COnsetMarkerLabel(tic.COnsetOffsetMs - ticAMs),
            secondLabelY);

        if (toc == null)
        {
            SetMarker(MarkerTocA, null, "");
            SetMarker(MarkerTocCPeak, null, "");
            SetMarker(MarkerTocCOnset, null, "");
            return;
        }

        double tocBaseMs = TocBaseMs(tic, toc);
        double tocAMs = toc.AOffsetMs;
        SetMarker(MarkerTocA, tocBaseMs + tocAMs, EscapementReadout.AMarkerLabel, topLabelY);
        SetMarker(
            MarkerTocCPeak,
            toc.CPeakValid ? tocBaseMs + toc.CPeakOffsetMs : null,
            EscapementReadout.CPeakMarkerLabel(toc.CPeakOffsetMs - tocAMs),
            topLabelY);
        SetMarker(
            MarkerTocCOnset,
            toc.COnsetValid ? tocBaseMs + toc.COnsetOffsetMs : null,
            EscapementReadout.COnsetMarkerLabel(toc.COnsetOffsetMs - tocAMs),
            secondLabelY);
    }

    private void UpdateReadout(BeatSegment? latest)
    {
        string[] values = EscapementReadout.Values(latest, _tracker);
        for (int i = 0; i < _valueTexts.Length && i < values.Length; i++)
        {
            _valueTexts[i].Text = values[i];
        }
    }

    private static VerticalLine AddMarker(Plot plot, LinePattern pattern)
    {
        VerticalLine marker = plot.Add.VerticalLine(0.0);
        marker.LineWidth = 1;
        marker.LinePattern = pattern;
        marker.IsVisible = false;
        marker.EnableAutoscale = false;
        return marker;
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

    private void SetMarker(int index, double? x, string text, double labelY = 0.0)
    {
        VerticalLine? marker = _markers[index];
        Text? label = _labels[index];
        if (marker == null || label == null)
        {
            return;
        }

        bool visible = x is not null;
        marker.IsVisible = visible;
        label.IsVisible = visible;
        if (x is double position)
        {
            marker.X = position;
            label.LabelText = text;
            label.Location = new Coordinates(position, labelY);
        }
    }

    private Text AddSignalQualityLabel(Plot plot)
    {
        Text label = plot.Add.Text("", 0.0, 0.0);
        label.LabelFontName = _textFontFamily;
        label.LabelFontSize = 13;
        label.LabelBold = true;
        label.Alignment = Alignment.UpperRight;
        label.IsVisible = false;
        return label;
    }

    private void SetSignalQuality(SignalQualityFlags quality)
    {
        _ = quality;
        if (_signalQualityLabel != null)
        {
            _signalQualityLabel.IsVisible = false;
        }
    }
    private void ApplySeriesTheme()
    {
        if (_envelopeScatter != null)
        {
            _envelopeScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        if (_rawMinScatter != null)
        {
            _rawMinScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        // Color by event type, not beat: A = tick green, C = tock red — the same
        // themed event color mapping the scope markers use
        // (RateScopeRenderer.ThemeColor) — so both beats read consistently.
        for (int i = 0; i < MarkerCount; i++)
        {
            uint color = IsAMarker(i) ? _theme.TraceTick : _theme.TraceTock;
            if (_markers[i] != null)
            {
                _markers[i]!.LineColor = Color.FromARGB(color);
            }

            if (_labels[i] != null)
            {
                _labels[i]!.LabelFontColor = Color.FromARGB(color);
            }
        }
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
