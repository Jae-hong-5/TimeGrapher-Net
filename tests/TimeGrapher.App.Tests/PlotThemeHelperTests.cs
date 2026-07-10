using ScottPlot;
using ScottPlot.Plottables;
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

    public PlotThemeHelperTests()
    {
        PlotThemeHelper.ConfigureFonts();
        HeadlessPlatform.EnsureStarted();
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BundledResolverLoadsD2CodingFromAvaloniaAssets(bool bold)
    {
        var resolver = new BundledPlotFontResolver();

        using SKTypeface typeface = Assert.IsType<SKTypeface>(
            resolver.CreateTypeface(PlotThemeHelper.GraphFontFamily, bold, italic: false));

        Assert.Equal(PlotThemeHelper.GraphFontFamily, typeface.FamilyName);
    }

    [Fact]
    public void BundledResolverDeclinesUnmatchedTypefaceRequests()
    {
        var resolver = new BundledPlotFontResolver();

        Assert.Null(resolver.CreateTypeface("Other Font", bold: false, italic: false));
        Assert.Null(resolver.CreateTypeface(PlotThemeHelper.GraphFontFamily, bold: false, italic: true));
    }

    [Fact]
    public void ConfiguredFontAppliesToPlotChromeAndNewText()
    {
        var plot = new Plot();

        PlotThemeHelper.Apply(plot, Palette);
        Text text = plot.Add.Text("graph text", 0, 0);

        Assert.Equal(PlotThemeHelper.GraphFontFamily, Fonts.Default);
        Assert.IsType<BundledPlotFontResolver>(Fonts.FontResolvers[0]);
        Assert.All(plot.Axes.GetAxes(), axis =>
        {
            Assert.Equal(PlotThemeHelper.GraphFontFamily, axis.Label.FontName);
            Assert.Equal(PlotThemeHelper.GraphFontFamily, axis.TickLabelStyle.FontName);
        });
        Assert.Equal(PlotThemeHelper.GraphFontFamily, text.LabelFontName);
    }

    [Fact]
    public void ApplyRendersBundledGraphTextPixels()
    {
        var plot = new Plot();
        PlotThemeHelper.Apply(plot, Palette);
        plot.Axes.SetLimits(-1, 1, -1, 1);
        Text text = plot.Add.Text("Graph 123", 0, 0);
        text.LabelAlignment = Alignment.MiddleCenter;
        text.LabelFontColor = Colors.Black;
        text.LabelFontSize = 24;

        var imageInfo = new SKImageInfo(320, 160, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(imageInfo);
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);

        plot.Render(canvas, imageInfo.Width, imageInfo.Height);

        bool hasTextPixel = false;
        for (int y = 50; y < 110 && !hasTextPixel; y++)
        {
            for (int x = 90; x < 230; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 64 && pixel.Green < 64 && pixel.Blue < 64)
                {
                    hasTextPixel = true;
                    break;
                }
            }
        }

        Assert.True(hasTextPixel, "The bundled D2Coding typeface did not render the graph label.");
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
