using ScottPlot;
using SkiaSharp;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class PlotThemeHelperTests
{
    [Fact]
    public void ApplyRendersTransparentFigureBackground()
    {
        var plot = new Plot();
        var palette = new PlotThemePalette(
            SurfaceBg: 0xFFE2E3E4,
            ScopeBg: 0xFFFFFFFF,
            ScopeGrid: 0xFFEAEAEA,
            TextPrimary: 0xFF1A1A1A,
            TraceWave: 0xFF6B6B6B,
            TraceTick: 0xFF2C9118,
            TraceTock: 0xFFD22222);

        PlotThemeHelper.Apply(plot, palette);

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
}
