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
    /// Expands an autoscaled Y range so it always spans at least
    /// [<paramref name="minBottom"/>, <paramref name="maxTop"/>]: autoscale may grow
    /// the range when the data exceeds the floor, but never zoom in tighter, so a
    /// small signal is not magnified into apparent chaos. Pure for unit testing.
    /// </summary>
    public static (double Bottom, double Top) ExpandToInclude(
        double autoBottom, double autoTop, double minBottom, double maxTop)
        => (Math.Min(autoBottom, minBottom), Math.Max(autoTop, maxTop));

    /// <summary>
    /// Applies <see cref="ExpandToInclude"/> to a plot's current Y limits — call
    /// right after AutoScale. The auto-fit is kept whenever the data is taller than
    /// the floor; otherwise the view is held open to the floor.
    /// </summary>
    public static void EnsureMinimumYRange(Plot plot, double minBottom, double maxTop)
    {
        AxisLimits limits = plot.Axes.GetLimits();
        (double bottom, double top) = ExpandToInclude(limits.Bottom, limits.Top, minBottom, maxTop);
        plot.Axes.SetLimitsY(bottom, top);
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
}
