using System.Globalization;
using ScottPlot;
using ScottPlot.Plottables;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class AveragePeriodRateAnnotations
{
    private const double LabelTopPaddingFraction = 0.08;
    private const float BoundaryLineWidth = 1.0f;

    private readonly string _fontFamily;
    private readonly bool _showLabels;
    private readonly List<VerticalLine> _boundaries = new();
    private readonly List<Text> _labels = new();
    private PlotThemePalette _theme = PlotThemePalette.Current;

    public AveragePeriodRateAnnotations(string fontFamily, bool showLabels = true)
    {
        _fontFamily = fontFamily;
        _showLabels = showLabels;
    }

    public void Reset()
    {
        _boundaries.Clear();
        _labels.Clear();
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        for (int i = 0; i < _boundaries.Count; i++)
        {
            ApplyBoundaryTheme(_boundaries[i], i);
        }

        for (int i = 0; i < _labels.Count; i++)
        {
            ApplyLabelTheme(_labels[i]);
        }
    }

    public void Update(
        Plot plot,
        IReadOnlyList<AveragePeriodRateInterval> intervals)
    {
        int boundariesUsed = 0;
        int labelsUsed = 0;
        int intervalIndex = 0;
        AxisLimits limits = plot.Axes.GetLimits();
        double labelY = limits.Top - (limits.Top - limits.Bottom) * LabelTopPaddingFraction;
        double previousBoundary = double.NaN;

        foreach (AveragePeriodRateInterval interval in intervals)
        {
            double start = interval.StartBeatIndex;
            double end = interval.EndBeatIndex;
            if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start)
            {
                continue;
            }

            if (!SameBoundary(previousBoundary, start))
            {
                VerticalLine startBoundary = EnsureBoundary(plot, boundariesUsed);
                startBoundary.X = start;
                startBoundary.IsVisible = true;
                ApplyBoundaryTheme(startBoundary, intervalIndex);
                boundariesUsed++;
                previousBoundary = start;
            }

            VerticalLine endBoundary = EnsureBoundary(plot, boundariesUsed);
            endBoundary.X = end;
            endBoundary.IsVisible = true;
            ApplyBoundaryTheme(endBoundary, intervalIndex);
            boundariesUsed++;
            previousBoundary = end;

            if (_showLabels)
            {
                Text label = EnsureLabel(plot, labelsUsed);
                label.LabelText = FormatLabel(interval);
                label.Location = new Coordinates((start + end) * 0.5, labelY);
                label.IsVisible = true;
                ApplyLabelTheme(label);
                labelsUsed++;
            }

            intervalIndex++;
        }

        for (int i = boundariesUsed; i < _boundaries.Count; i++)
        {
            _boundaries[i].IsVisible = false;
        }

        for (int i = labelsUsed; i < _labels.Count; i++)
        {
            _labels[i].IsVisible = false;
        }
    }

    private VerticalLine EnsureBoundary(Plot plot, int index)
    {
        while (_boundaries.Count <= index)
        {
            VerticalLine boundary = plot.Add.VerticalLine(0.0);
            boundary.LineWidth = BoundaryLineWidth;
            boundary.LinePattern = LinePattern.Dashed;
            boundary.IsVisible = false;
            boundary.EnableAutoscale = false;
            _boundaries.Add(boundary);
            ApplyBoundaryTheme(boundary, _boundaries.Count - 1);
        }

        return _boundaries[index];
    }

    private Text EnsureLabel(Plot plot, int index)
    {
        while (_labels.Count <= index)
        {
            Text label = plot.Add.Text(string.Empty, 0.0, 0.0);
            label.LabelFontName = _fontFamily;
            label.LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
            label.LabelBold = true;
            label.LabelAlignment = Alignment.UpperCenter;
            label.Alignment = Alignment.UpperCenter;
            label.IsVisible = false;
            _labels.Add(label);
            ApplyLabelTheme(label);
        }

        return _labels[index];
    }

    private void ApplyBoundaryTheme(VerticalLine boundary, int intervalIndex)
    {
        boundary.Color = Color.FromARGB(AnnotationColor(intervalIndex));
    }

    private void ApplyLabelTheme(Text label)
    {
        label.LabelFontColor = Color.FromARGB(_theme.TextPrimary);
    }

    private uint AnnotationColor(int intervalIndex) => intervalIndex % 2 == 0
        ? _theme.AveragePeriodAnnotation
        : _theme.AveragePeriodAnnotationAlternate;

    private static bool SameBoundary(double left, double right) =>
        double.IsFinite(left) && Math.Abs(left - right) <= 1e-9;

    private static string FormatRate(double rateSPerDay)
    {
        string sign = rateSPerDay < 0.0 ? "-" : "+";
        return sign + Math.Abs(rateSPerDay).ToString("F1", CultureInfo.InvariantCulture) + " s/d";
    }

    private static string FormatLabel(AveragePeriodRateInterval interval)
    {
        return FormatRate(interval.RateSPerDay) + "\n" +
               FormatAmplitude(interval) + "  " + FormatBeatError(interval);
    }

    private static string FormatAmplitude(AveragePeriodRateInterval interval)
    {
        if (!interval.AmplitudeValid)
        {
            return "---°";
        }

        long rounded = (long)Math.Round(interval.AmplitudeDeg, MidpointRounding.AwayFromZero);
        return rounded.ToString(CultureInfo.InvariantCulture) + "°";
    }

    private static string FormatBeatError(AveragePeriodRateInterval interval)
    {
        return interval.BeatErrorValid
            ? interval.BeatErrorMs.ToString("F1", CultureInfo.InvariantCulture) + " ms"
            : "---- ms";
    }
}
