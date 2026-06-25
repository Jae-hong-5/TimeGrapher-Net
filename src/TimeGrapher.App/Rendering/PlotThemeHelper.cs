using ScottPlot;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Shared ScottPlot chrome theming (backgrounds, axes, grid) every plot tab
/// applies. One definition instead of the per-renderer copies that had already
/// started to drift, so a palette change recolors every tab the same way.
/// </summary>
internal static class PlotThemeHelper
{
    /// <summary>
    /// Graph label font size shared by every ScottPlot renderer. Mirrors the
    /// App.axaml base FontSize (14); ScottPlot label sizes cannot bind to Avalonia
    /// resources, so this single code constant replaces the per-file literals that
    /// had drifted (10-16) before standardization.
    /// </summary>
    public const float GraphLabelFontSize = 14f;
    public const float CompactLeftAxisSizePx = 44f;
    public const float CompactBottomAxisSizePx = 34f;

    public static void Apply(Plot plot, PlotThemePalette theme)
    {
        plot.FigureBackground.Color = Colors.Transparent;
        plot.DataBackground.Color = Color.FromARGB(theme.ScopeBg);
        plot.Axes.Color(Color.FromARGB(theme.TextPrimary));
        plot.Axes.FrameColor(Color.FromARGB(theme.ScopeGrid));
        plot.Grid.MajorLineColor = Color.FromARGB(theme.ScopeGrid);
        plot.Grid.MinorLineColor = Color.FromARGB(theme.ScopeGrid);
    }

    public static void ApplyCompactAxisPanels(Plot plot)
    {
        plot.Axes.Left.MinimumSize = CompactLeftAxisSizePx;
        plot.Axes.Left.MaximumSize = CompactLeftAxisSizePx;
        plot.Axes.Bottom.MinimumSize = CompactBottomAxisSizePx;
        plot.Axes.Bottom.MaximumSize = CompactBottomAxisSizePx;
    }
}
