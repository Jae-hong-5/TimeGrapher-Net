using ScottPlot;
using ScottPlot.Rendering;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Shared axis-rule setup for plots whose X axis has a meaningful left bound
/// below which the range is empty — elapsed time, absolute sample ticks, sweep
/// phase, a beat index, or window ms. Floors the view's left edge at that bound
/// so panning and zoom-out never reveal the empty region beyond it. Y stays
/// unconstrained (rate and beat error are signed). One definition instead of
/// per-renderer copies, mirroring <see cref="PlotThemeHelper"/>.
/// </summary>
internal static class PlotAxisRules
{
    /// <summary>Floors the X view's left edge at 0 (the common zero-origin case).</summary>
    public static void ClampLeftEdgeToZero(Plot plot) => ClampLeftEdge(plot, 0);

    /// <summary>
    /// Floors the X view's left edge at 0 like <see cref="ClampLeftEdgeToZero"/>,
    /// but shifts the right edge along so the view span is preserved (a pan that
    /// reaches the origin stops there instead of collapsing the window from the
    /// right). Use where the window must never cross below the origin on either
    /// edge — e.g. the Multi-Filter Scope, whose lanes share one X window: a
    /// collapsed/negative window on one lane would otherwise be linked onto the
    /// others.
    /// </summary>
    public static void ClampLeftEdgePreservingSpan(Plot plot)
    {
        plot.Axes.Rules.Clear();
        plot.Axes.Rules.Add(new LeftEdgeWall(plot.Axes.Bottom, 0));
    }

    /// <summary>
    /// Installs a rule that floors the X view's left edge at <paramref name="minLeft"/>
    /// on every render. Clears existing rules first so repeated CreateGraphs
    /// calls do not accumulate duplicates.
    /// </summary>
    public static void ClampLeftEdge(Plot plot, double minLeft)
    {
        plot.Axes.Rules.Clear();
        plot.Axes.Rules.Add(new LeftEdgeFloor(plot.Axes.Bottom, minLeft));
    }

    /// <summary>
    /// Floors the X view's left edge whenever pan/zoom-out would carry it below
    /// the bound, leaving the right edge untouched. ScottPlot's built-in
    /// MaximumBoundary instead preserves the view span by shifting the whole
    /// window right, which is wrong for the live scope panes whose window sits
    /// far from the origin on the absolute sample-tick axis — there it would
    /// jump the view back toward the bound rather than simply stop the edge.
    /// </summary>
    private sealed class LeftEdgeFloor : IAxisRule
    {
        private readonly IXAxis _xAxis;
        private readonly double _minLeft;

        public LeftEdgeFloor(IXAxis xAxis, double minLeft)
        {
            _xAxis = xAxis;
            _minLeft = minLeft;
        }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            if (_xAxis.Range.Min < _minLeft)
            {
                _xAxis.Range.Min = _minLeft;
            }
        }
    }

    /// <summary>
    /// Floors the X view's left edge at <see cref="_minLeft"/> and carries the
    /// right edge with it (preserving the view span) whenever pan/zoom-out would
    /// push the left edge below the bound. Unlike <see cref="LeftEdgeFloor"/>,
    /// which pins only the left edge and lets a continued left pan shrink — and
    /// eventually invert — the window from the right, this stops the whole view
    /// at the bound like a wall.
    /// </summary>
    private sealed class LeftEdgeWall : IAxisRule
    {
        private readonly IXAxis _xAxis;
        private readonly double _minLeft;

        public LeftEdgeWall(IXAxis xAxis, double minLeft)
        {
            _xAxis = xAxis;
            _minLeft = minLeft;
        }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            if (_xAxis.Range.Min < _minLeft)
            {
                double span = _xAxis.Range.Max - _xAxis.Range.Min;
                _xAxis.Range.Min = _minLeft;
                _xAxis.Range.Max = _minLeft + span;
            }
        }
    }
}
