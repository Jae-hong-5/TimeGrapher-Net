using System.Globalization;
using ScottPlot;
using ScottPlot.Plottables;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class AveragePeriodRateOverlay
{
    private const byte FillAlpha = 34;
    private const double LabelTopPaddingFraction = 0.08;

    private readonly string _fontFamily;
    private readonly List<HorizontalSpan> _spans = new();
    private readonly List<Text> _labels = new();
    private PlotThemePalette _theme = PlotThemePalette.Current;

    public AveragePeriodRateOverlay(string fontFamily)
    {
        _fontFamily = fontFamily;
    }

    public void Reset()
    {
        _spans.Clear();
        _labels.Clear();
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        for (int i = 0; i < _spans.Count; i++)
        {
            ApplyTheme(_spans[i], _labels[i], i);
        }
    }

    public void Update(
        Plot plot,
        IReadOnlyList<AveragePeriodRateInterval> intervals,
        double pageLeft,
        double pageRight)
    {
        int used = 0;
        AxisLimits limits = plot.Axes.GetLimits();
        double labelY = limits.Top - (limits.Top - limits.Bottom) * LabelTopPaddingFraction;

        foreach (AveragePeriodRateInterval interval in intervals)
        {
            if (interval.EndBeatIndex <= pageLeft || interval.StartBeatIndex >= pageRight)
            {
                continue;
            }

            double start = Math.Max(interval.StartBeatIndex, pageLeft);
            double end = Math.Min(interval.EndBeatIndex, pageRight);
            if (end <= start)
            {
                continue;
            }

            Ensure(plot, used);
            HorizontalSpan span = _spans[used];
            Text label = _labels[used];
            span.X1 = start;
            span.X2 = end;
            span.IsVisible = true;

            label.LabelText = FormatRate(interval.RateSPerDay);
            label.Location = new Coordinates((start + end) * 0.5, labelY);
            label.IsVisible = true;
            ApplyTheme(span, label, used);
            used++;
        }

        for (int i = used; i < _spans.Count; i++)
        {
            _spans[i].IsVisible = false;
            _labels[i].IsVisible = false;
        }
    }

    private void Ensure(Plot plot, int index)
    {
        while (_spans.Count <= index)
        {
            HorizontalSpan span = plot.Add.HorizontalSpan(0.0, 0.0);
            Text label = plot.Add.Text(string.Empty, 0.0, 0.0);
            label.LabelFontName = _fontFamily;
            label.LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
            label.LabelBold = true;
            label.LabelAlignment = Alignment.UpperCenter;
            span.LineStyle.IsVisible = false;
            _spans.Add(span);
            _labels.Add(label);
            ApplyTheme(span, label, _spans.Count - 1);
        }
    }

    private void ApplyTheme(HorizontalSpan span, Text label, int intervalIndex)
    {
        uint fillColor = intervalIndex % 2 == 0
            ? _theme.AveragePeriodOverlayFill
            : _theme.AveragePeriodOverlayAlternateFill;
        span.FillStyle.Color = Color.FromARGB(fillColor).WithAlpha(FillAlpha);
        label.LabelFontColor = Color.FromARGB(_theme.TextPrimary);
    }

    private static string FormatRate(double rateSPerDay)
    {
        string sign = rateSPerDay < 0.0 ? "-" : "+";
        return sign + Math.Abs(rateSPerDay).ToString("F1", CultureInfo.InvariantCulture) + " s/d";
    }
}
