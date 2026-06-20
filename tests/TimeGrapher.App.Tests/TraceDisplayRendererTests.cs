using Avalonia.Controls;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;
using PlotColor = ScottPlot.Color;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the fixed axis-panel sizing (the RateScopeRenderer tactic): the rate and
/// amplitude plots must keep a constant-width, mutually aligned data area
/// regardless of how wide their Y tick labels grow or how large the control is
/// (e.g. a maximized window), so the graph never resizes itself at runtime.
/// </summary>
public sealed class TraceDisplayRendererTests
{
    private const float ExpectedLeftPanel = 60f;
    private const float ExpectedBottomPanel = 42f;
    private const float ExpectedHiddenBottomPanel = 10f;

    private static MetricsHistorySeries Series(double[] x, double[] y) => new()
    {
        X = x,
        Y = y,
        YMin = y,
        YMax = y,
    };

    private static TraceDisplayRenderer NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot)
    {
        ratePlot = new AvaPlot();
        amplitudePlot = new AvaPlot();
        return new TraceDisplayRenderer(
            ratePlot, amplitudePlot, new Border(), new TextBlock(), new TextBlock());
    }

    private static AnalysisFrame Frame(ulong version, double[] rateY, double[] amplitudeY)
    {
        double[] x = { 0.0, 100.0, 200.0, 300.0 };
        return new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                Version = version,
                Rate = Series(x, rateY),
                Amplitude = Series(x, amplitudeY),
                RateValid = true,
                AmplitudeValid = true,
            },
        };
    }

    private static AnalysisFrame EmptyFrame(ulong version) => new()
    {
        MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = version,
            Rate = Series(Array.Empty<double>(), Array.Empty<double>()),
            Amplitude = Series(Array.Empty<double>(), Array.Empty<double>()),
            RateValid = false,
            AmplitudeValid = false,
        },
    };

    private static float LeftPanel(AvaPlot plot, int width, int height)
    {
        plot.Plot.RenderInMemory(width, height);
        var render = plot.Plot.RenderManager.LastRender;
        return render.DataRect.Left - render.FigureRect.Left;
    }

    [Fact]
    public void CreateGraphs_PinsLeftPanelAndSharesTimeAxisOnBottomPane()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);

        renderer.CreateGraphs();

        foreach (AvaPlot plot in new[] { ratePlot, amplitudePlot })
        {
            Assert.Equal(ExpectedLeftPanel, plot.Plot.Axes.Left.MinimumSize);
            Assert.Equal(ExpectedLeftPanel, plot.Plot.Axes.Left.MaximumSize);
        }

        // Amplitude (bottom) carries the shared time axis; rate (top) hides its X
        // axis and reserves a small bottom panel, the Long-Term stacked pattern.
        Assert.Equal(ExpectedBottomPanel, amplitudePlot.Plot.Axes.Bottom.MinimumSize);
        Assert.Equal(ExpectedBottomPanel, amplitudePlot.Plot.Axes.Bottom.MaximumSize);
        Assert.True(amplitudePlot.Plot.Axes.Bottom.TickLabelStyle.IsVisible);
        Assert.Equal(ExpectedHiddenBottomPanel, ratePlot.Plot.Axes.Bottom.MinimumSize);
        Assert.Equal(ExpectedHiddenBottomPanel, ratePlot.Plot.Axes.Bottom.MaximumSize);
        Assert.False(ratePlot.Plot.Axes.Bottom.TickLabelStyle.IsVisible);
    }

    [Fact]
    public void DataArea_StaysConstantWidthAndAlignedAcrossLabelGrowthAndControlSize()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        renderer.CreateGraphs();

        // Narrow labels, small control.
        renderer.RenderFrame(
            Frame(1, new[] { 1.0, 2.0, 1.0, 2.0 }, new[] { 281.0, 282.0, 281.0, 282.0 }),
            new AnalysisTabRenderContext(48000));
        float rateNarrow = LeftPanel(ratePlot, 900, 240);
        float ampNarrow = LeftPanel(amplitudePlot, 900, 240);

        // Wider labels (signed two-digit rate, wide amplitude swing) and a maximized
        // control: the pinned panel must keep the same width on both axes.
        renderer.RenderFrame(
            Frame(2, new[] { -15.0, 14.0, -11.0, 9.0 }, new[] { 268.0, 311.0, 269.0, 305.0 }),
            new AnalysisTabRenderContext(48000));
        float rateWideBig = LeftPanel(ratePlot, 1900, 980);
        float ampWideBig = LeftPanel(amplitudePlot, 1900, 980);

        Assert.Equal(ExpectedLeftPanel, rateNarrow, 1f);
        Assert.Equal(ExpectedLeftPanel, ampNarrow, 1f);
        // Constant across label growth + control resize, and equal between the two
        // stacked plots (left edges aligned).
        Assert.Equal(rateNarrow, rateWideBig, 1f);
        Assert.Equal(ampNarrow, ampWideBig, 1f);
        Assert.Equal(rateWideBig, ampWideBig, 1f);
    }

    [Fact]
    public void ApplyTheme_ColorsBandsLinesAndLimitsByMeasure()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        var palette = new PlotThemePalette(
            SurfaceBg: 0xFF101010,
            ScopeBg: 0xFF202020,
            ScopeGrid: 0xFF303030,
            TextPrimary: 0xFF404040,
            TraceWave: 0xFF505050,
            TraceTick: 0xFF116611,
            TraceTock: 0xFF606060,
            VarioMinMax: 0xFF2266CC,
            VarioBad: 0xFFCC2233);

        renderer.CreateGraphs();
        renderer.ApplyTheme(palette);

        // Rate -> bad-deviation color, amplitude -> min/max color, both as a
        // borderless shaded band behind the line (the Long-Term graph style).
        Assert.Equal(PlotColor.FromARGB(0xFFCC2233), Line(ratePlot).LineColor);
        Assert.Equal(PlotColor.FromARGB(0xFF2266CC), Line(amplitudePlot).LineColor);
        Assert.Equal(PlotColor.FromARGB(0xFFCC2233).WithAlpha(42), Band(ratePlot).FillStyle.Color);
        Assert.Equal(PlotColor.FromARGB(0xFF2266CC).WithAlpha(42), Band(amplitudePlot).FillStyle.Color);
        Assert.Equal(0, Band(ratePlot).LineStyle.Width);
        Assert.Equal(0, Band(amplitudePlot).LineStyle.Width);
        // Both limit labels on each plot carry the per-measure color.
        Assert.All(ratePlot.Plot.GetPlottables<Text>(), t => Assert.Equal(PlotColor.FromARGB(0xFFCC2233), t.LabelFontColor));
        Assert.All(amplitudePlot.Plot.GetPlottables<Text>(), t => Assert.Equal(PlotColor.FromARGB(0xFF2266CC), t.LabelFontColor));
    }

    [Fact]
    public void AcceptLimitLabels_HiddenUntilFirstBeatThenShown()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);

        renderer.CreateGraphs();

        // Before any beat: limit numbers hidden (a right-edge label would float in
        // an empty plot); the shaded band still marks the target zone.
        Assert.Empty(VisibleTextLabels(ratePlot));
        Assert.Empty(VisibleTextLabels(amplitudePlot));
        Assert.True(Band(ratePlot).IsVisible);
        Assert.True(Band(amplitudePlot).IsVisible);

        // An empty but freshly-versioned frame exercises the hasData gate (not just
        // the creation default): labels must stay hidden.
        renderer.RenderFrame(EmptyFrame(1), new AnalysisTabRenderContext(48000));
        Assert.Empty(VisibleTextLabels(ratePlot));
        Assert.Empty(VisibleTextLabels(amplitudePlot));

        renderer.RenderFrame(
            Frame(2, new[] { 1.0, 2.0, 1.0, 2.0 }, new[] { 281.0, 282.0, 281.0, 282.0 }),
            new AnalysisTabRenderContext(48000));

        Assert.Equal(new[] { "-10", "+10" }, VisibleTextLabels(ratePlot));
        Assert.Equal(new[] { "270", "300" }, VisibleTextLabels(amplitudePlot));
    }

    [Fact]
    public void DataAreaHeightsMatchAtRowRatioDespiteAsymmetricBottomReserve()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        renderer.CreateGraphs();
        renderer.RenderFrame(
            Frame(1, new[] { 1.0, 2.0, 1.0, 2.0 }, new[] { 281.0, 282.0, 281.0, 282.0 }),
            new AnalysisTabRenderContext(48000));

        // The tab grid gives the amplitude row 1.11x the rate row; at the design
        // star-row height the asymmetric bottom reserve (10px rate vs 42px amplitude)
        // is exactly offset so both DATA areas are equal. Rendering each pane at the
        // 1.11 ratio reproduces that.
        const int rateH = 291;
        int ampH = (int)Math.Round(rateH * 1.11);
        ratePlot.Plot.RenderInMemory(900, rateH);
        var rateRender = ratePlot.Plot.RenderManager.LastRender;
        amplitudePlot.Plot.RenderInMemory(900, ampH);
        var ampRender = amplitudePlot.Plot.RenderManager.LastRender;

        float rateBottom = rateRender.FigureRect.Bottom - rateRender.DataRect.Bottom;
        float ampBottom = ampRender.FigureRect.Bottom - ampRender.DataRect.Bottom;
        Assert.Equal(32f, ampBottom - rateBottom, 2f);
        Assert.Equal(rateRender.DataRect.Height, ampRender.DataRect.Height, 4f);
    }

    private static Scatter Line(AvaPlot plot) => plot.Plot.GetPlottables<Scatter>().Single();

    private static VerticalSpan Band(AvaPlot plot) => plot.Plot.GetPlottables<VerticalSpan>().Single();

    private static string[] VisibleTextLabels(AvaPlot plot) =>
        plot.Plot.GetPlottables<Text>()
            .Where(text => text.IsVisible)
            .Select(text => text.LabelText)
            .ToArray();
}
