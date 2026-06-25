using ScottPlot;
using SkiaSharp;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class PlotThemeHelperTests
{
    private static readonly PlotThemePalette Palette = new(
        SurfaceBg: 0xFFE2E3E4,
        ScopeBg: 0xFFFFFFFF,
        ScopeGrid: 0xFFEAEAEA,
        TextPrimary: 0xFF1A1A1A,
        TraceWave: 0xFF6B6B6B,
        TraceTick: 0xFF2C9118,
        TraceTock: 0xFFD22222);

    [Fact]
    public void ApplyRendersTransparentFigureBackground()
    {
        var plot = new Plot();

        PlotThemeHelper.Apply(plot, Palette);

        var imageInfo = new SKImageInfo(320, 240, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(imageInfo);
        bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap);

        plot.Render(canvas, imageInfo.Width, imageInfo.Height);

        Assert.Equal(0, bitmap.GetPixel(1, 1).Alpha);
        Assert.Equal(byte.MaxValue, bitmap.GetPixel(imageInfo.Width / 2, imageInfo.Height / 2).Alpha);
    }

    [Fact]
    public void ApplyCompactAxisPanelsPinsLeftAndBottomSizes()
    {
        var plot = new Plot();

        PlotThemeHelper.ApplyCompactAxisPanels(plot);

        Assert.Equal(PlotThemeHelper.CompactLeftAxisSizePx, plot.Axes.Left.MinimumSize);
        Assert.Equal(PlotThemeHelper.CompactLeftAxisSizePx, plot.Axes.Left.MaximumSize);
        Assert.Equal(PlotThemeHelper.CompactBottomAxisSizePx, plot.Axes.Bottom.MinimumSize);
        Assert.Equal(PlotThemeHelper.CompactBottomAxisSizePx, plot.Axes.Bottom.MaximumSize);
    }

    [Fact]
    public void ApplyCompactAxisPanelsExpandsRenderedDataRectangle()
    {
        var baseline = LabeledPlot();
        var compact = LabeledPlot();
        PlotThemeHelper.ApplyCompactAxisPanels(compact);

        (float baselineWidth, float baselineHeight) = RenderedDataSize(baseline);
        (float compactWidth, float compactHeight) = RenderedDataSize(compact);

        Assert.True(compactWidth > baselineWidth);
        Assert.True(compactHeight > baselineHeight);
    }

    private static Plot LabeledPlot()
    {
        var plot = new Plot();
        PlotThemeHelper.Apply(plot, Palette);
        plot.XLabel("Sweep (ms)");
        plot.YLabel("Signal Level");
        plot.Add.Scatter(new[] { 0.0, 1.0 }, new[] { -1.0, 1.0 });
        return plot;
    }

    private static (float Width, float Height) RenderedDataSize(Plot plot)
    {
        plot.RenderInMemory(900, 540);
        var render = plot.RenderManager.LastRender;
        return (render.DataRect.Width, render.DataRect.Height);
    }
}
