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
/// BeatSegmentsSnapshot the frame carries (the Beat-Noise Scope's segment
/// infrastructure reused for fine-grained intra-beat timing).
///
/// One large plot shows the latest beat's real raw waveform — the un-rectified
/// bipolar signal as captured, drawn as the per-point min/max outlines so the
/// vertically-symmetric scope look is the actual data, not the negated envelope
/// (it falls back to the rectified envelope only when the producer fed no raw).
/// Over it sit pooled vertical timing markers and millisecond labels for the
/// escapement-cycle events: A (green dashed, the cycle's zero reference), C peak
/// (red dashed) and C onset (red dotted, only when the detector located the
/// cluster's rising edge). The
/// numeric panel below reads the current A→C interval per reference, the
/// onset-vs-peak delta, and — via EscapementTimingTracker fed here on each
/// snapshot-version change — the windowed mean±sigma of both references plus
/// which reference is more repeatable.
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
    private VerticalLine? _aMarker;
    private VerticalLine? _cPeakMarker;
    private VerticalLine? _cOnsetMarker;
    private Text? _aLabel;
    private Text? _cPeakLabel;
    private Text? _cOnsetLabel;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private ulong _lastObservedVersion;

    public EscapementAnalyzerRenderer(AvaPlot plot, TextBlock[] valueTexts, string textFontFamily)
    {
        _plot = plot;
        _valueTexts = valueTexts;
        _textFontFamily = textFontFamily;
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
        plot.YLabel("Amplitude");
        plot.XLabel("ms");
        _envelopeScatter = plot.Add.Scatter(_envelopeX, _envelopeY);
        _envelopeScatter.LineWidth = 1;
        _envelopeScatter.MarkerStyle.IsVisible = false;
        _rawMinScatter = plot.Add.Scatter(_envelopeX, _rawMinY);
        _rawMinScatter.LineWidth = 1;
        _rawMinScatter.MarkerStyle.IsVisible = false;
        _rawMinScatter.IsVisible = false;
        _aMarker = AddMarker(plot, LinePattern.Dashed);
        _cPeakMarker = AddMarker(plot, LinePattern.Dashed);
        _cOnsetMarker = AddMarker(plot, LinePattern.Dotted);
        _aLabel = AddLabel(plot);
        _cPeakLabel = AddLabel(plot);
        _cOnsetLabel = AddLabel(plot);
        plot.Axes.SetLimitsX(0, BeatSegmentCapture.WindowMs);
        PlotAxisRules.ClampLeftEdgeToZero(plot);

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
        // Review cursor deliberately not rendered here: this is a single-beat
        // (latest segment) inspection view whose x-domain is milliseconds
        // within that beat's window, not stream time, so
        // context.ReviewCursorTimeS has no meaningful x mapping on this plot.
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

        BeatSegment? latest = snapshot.Segments.Count > 0 ? snapshot.Segments[^1] : null;
        double labelExtent = RenderTrace(latest);
        UpdateMarkers(latest, labelExtent);
        UpdateReadout(latest);
        _plot.Refresh();
    }

    /// <summary>
    /// Refills the trace series and rescales the axes; returns the amplitude the
    /// marker labels sit above. Draws the real raw bipolar waveform (RawMin/Max
    /// outlines, symmetric about zero) when the segment carries raw, and falls
    /// back to the rectified envelope otherwise.
    /// </summary>
    private double RenderTrace(BeatSegment? segment)
    {
        _envelopeX.Clear();
        _envelopeY.Clear();
        _rawMinY.Clear();
        if (segment == null)
        {
            if (_rawMinScatter != null)
            {
                _rawMinScatter.IsVisible = false;
            }

            return 1.0;
        }

        return segment.RawValid ? RenderRaw(segment) : RenderEnvelope(segment);
    }

    /// <summary>Raw bipolar waveform: max outline up, min outline down, symmetric Y.</summary>
    private double RenderRaw(BeatSegment segment)
    {
        ReadOnlySpan<float> min = segment.RawMin.Span;
        ReadOnlySpan<float> max = segment.RawMax.Span;
        int count = Math.Min(min.Length, max.Length);
        double extent = 0.0;
        for (int i = 0; i < count; i++)
        {
            _envelopeX.Add(i * segment.MsPerPoint);
            _envelopeY.Add(max[i]);
            _rawMinY.Add(min[i]);
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

        _plot.Plot.Axes.SetLimitsX(0, segment.MsPerPoint * count);
        _plot.Plot.Axes.SetLimitsY(-YHeadroom * extent, YHeadroom * extent);
        return extent;
    }

    /// <summary>Fallback rectified envelope (no raw fed); returns the envelope max.</summary>
    private double RenderEnvelope(BeatSegment segment)
    {
        if (_rawMinScatter != null)
        {
            _rawMinScatter.IsVisible = false;
        }

        ReadOnlySpan<float> samples = segment.Samples.Span;
        double max = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            double y = samples[i];
            _envelopeX.Add(i * segment.MsPerPoint);
            _envelopeY.Add(y);
            if (y > max)
            {
                max = y;
            }
        }

        if (max <= 0.0)
        {
            max = 1.0;
        }

        _plot.Plot.Axes.SetLimitsX(0, segment.MsPerPoint * samples.Length);
        _plot.Plot.Axes.SetLimitsY(-0.02 * max, YHeadroom * max);
        return max;
    }

    private void UpdateMarkers(BeatSegment? segment, double envelopeMax)
    {
        if (segment == null)
        {
            SetMarker(_aMarker, _aLabel, null, "");
            SetMarker(_cPeakMarker, _cPeakLabel, null, "");
            SetMarker(_cOnsetMarker, _cOnsetLabel, null, "");
            return;
        }

        double topLabelY = TopLabelFraction * envelopeMax;
        double secondLabelY = SecondLabelFraction * envelopeMax;

        SetMarker(_aMarker, _aLabel, segment.AOffsetMs, EscapementReadout.AMarkerLabel, topLabelY);
        SetMarker(
            _cPeakMarker, _cPeakLabel,
            segment.CPeakValid ? segment.CPeakOffsetMs : null,
            EscapementReadout.CPeakMarkerLabel(segment.CPeakOffsetMs - segment.AOffsetMs),
            topLabelY);
        SetMarker(
            _cOnsetMarker, _cOnsetLabel,
            segment.COnsetValid ? segment.COnsetOffsetMs : null,
            EscapementReadout.COnsetMarkerLabel(segment.COnsetOffsetMs - segment.AOffsetMs),
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
        label.LabelFontSize = 11;
        label.Alignment = Alignment.UpperLeft;
        label.IsVisible = false;
        return label;
    }

    private static void SetMarker(
        VerticalLine? marker, Text? label, double? x, string text, double labelY = 0.0)
    {
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

        // A = tick green, C = tock red: the same themed event color mapping the
        // scope markers use (RateScopeRenderer.ThemeColor).
        if (_aMarker != null)
        {
            _aMarker.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        if (_aLabel != null)
        {
            _aLabel.LabelFontColor = Color.FromARGB(_theme.TraceTick);
        }

        foreach (VerticalLine? marker in new[] { _cPeakMarker, _cOnsetMarker })
        {
            if (marker != null)
            {
                marker.LineColor = Color.FromARGB(_theme.TraceTock);
            }
        }

        foreach (Text? label in new[] { _cPeakLabel, _cOnsetLabel })
        {
            if (label != null)
            {
                label.LabelFontColor = Color.FromARGB(_theme.TraceTock);
            }
        }
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
