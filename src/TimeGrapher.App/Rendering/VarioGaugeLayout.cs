namespace TimeGrapher.App.Rendering;

/// <summary>Horizontal text anchor for a gauge marker label.</summary>
internal enum GaugeLabelAnchor
{
    Left,
    Center,
    Right,
}

/// <summary>A marker label to render: its short role text, data position, and anchor.</summary>
internal readonly record struct GaugeLabel(string Role, double X, double Y, GaugeLabelAnchor Anchor);

/// <summary>
/// Decides where Vario gauge marker labels (min/max/avg/now) sit so same-value
/// markers remain readable on fixed 1280x800 layouts. Min/max share the top lane,
/// average uses the middle lane, and current uses the bottom lane; labels still
/// anchor inward near the axis edges so they do not clip.
/// </summary>
internal static class VarioGaugeLayout
{
    /// <summary>A label is treated as this fraction of the axis span wide; closer centres overlap.</summary>
    public const double LabelWidthFraction = 0.07;

    /// <summary>Markers within this fraction of an edge anchor inward so text cannot clip.</summary>
    public const double EdgeFraction = 0.05;

    public const double MinMaxLabelY = 1.29;
    public const double AverageLabelY = 1.18;
    public const double CurrentLabelY = 1.07;

    /// <summary>Labels to draw, ordered left-to-right, then top-to-bottom.</summary>
    public static IReadOnlyList<GaugeLabel> LayOut(
        double lo, double hi, double? min, double? max, double? avg, double? current)
    {
        double span = hi - lo;
        if (span <= 0.0)
        {
            return Array.Empty<GaugeLabel>();
        }

        double minGap = span * LabelWidthFraction;
        var labels = new List<GaugeLabel>(4);

        if (min is double mn && max is double mx)
        {
            if (Math.Abs(mx - mn) < minGap)
            {
                labels.Add(CreateLabel("min/max", Clamp((mn + mx) * 0.5, lo, hi), MinMaxLabelY, lo, hi, span));
            }
            else
            {
                labels.Add(CreateLabel("min", mn, MinMaxLabelY, lo, hi, span));
                labels.Add(CreateLabel("max", mx, MinMaxLabelY, lo, hi, span));
            }
        }
        else if (min is double onlyMin)
        {
            labels.Add(CreateLabel("min", onlyMin, MinMaxLabelY, lo, hi, span));
        }
        else if (max is double onlyMax)
        {
            labels.Add(CreateLabel("max", onlyMax, MinMaxLabelY, lo, hi, span));
        }

        if (avg is double a)
        {
            labels.Add(CreateLabel("avg", a, AverageLabelY, lo, hi, span));
        }

        if (current is double c)
        {
            labels.Add(CreateLabel("now", c, CurrentLabelY, lo, hi, span));
        }

        labels.Sort((l, r) =>
        {
            int xOrder = l.X.CompareTo(r.X);
            return xOrder != 0 ? xOrder : r.Y.CompareTo(l.Y);
        });
        return labels;
    }

    private static GaugeLabel CreateLabel(
        string role, double x, double y, double lo, double hi, double span)
    {
        double clampedX = Clamp(x, lo, hi);
        double edge = span * EdgeFraction;
        GaugeLabelAnchor anchor =
            clampedX <= lo + edge ? GaugeLabelAnchor.Left :
            clampedX >= hi - edge ? GaugeLabelAnchor.Right :
            GaugeLabelAnchor.Center;
        return new GaugeLabel(role, clampedX, y, anchor);
    }

    private static double Clamp(double value, double lo, double hi) =>
        Math.Min(Math.Max(value, lo), hi);
}
