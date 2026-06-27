using ScottPlot;
using ScottPlot.Plottables;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Shared pause-and-review scrub cursor: a vertical guide line at the
/// cursor position (in the owning plot's own x-domain). Implemented as a
/// ScottPlot <see cref="VerticalLine"/> because an axis line spans the viewport
/// at render time without contributing a Y extent to autoscaling — the previous
/// per-renderer LinePlot cursors spanned y = ±1e6 and blew the autoscaled Y fit
/// of every plot the moment the cursor became visible (pinned by
/// ScottPlotAutoScaleBehaviorTests).
/// </summary>
internal sealed class ReviewCursorLayer
{
    private readonly VerticalLine _line;

    public ReviewCursorLayer(Plot plot)
    {
        _line = plot.Add.VerticalLine(0.0);
        _line.LinePattern = GraphLinePatterns.VerticalGuide;
        _line.LineWidth = 2;
        _line.IsVisible = false;
        _line.EnableAutoscale = false;
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _line.Color = Color.FromARGB(theme.VarioBad);
    }

    /// <summary>
    /// Shows the cursor at <paramref name="cursorX"/> (plot x-domain units) or
    /// hides it when null. Returns true when something changed and the owning
    /// plot needs a refresh.
    /// </summary>
    public bool Update(double? cursorX)
    {
        bool visible = cursorX is not null;
        bool changed = false;

        if (_line.IsVisible != visible)
        {
            _line.IsVisible = visible;
            changed = true;
        }

        if (cursorX is double x && _line.X != x)
        {
            _line.X = x;
            changed = true;
        }

        return changed;
    }
}
