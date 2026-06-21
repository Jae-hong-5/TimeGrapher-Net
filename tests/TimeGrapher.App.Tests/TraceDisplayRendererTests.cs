using Avalonia.Controls;
using ScottPlot;
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
            ratePlot, amplitudePlot, new Border(), new TextBlock());
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
        // The accept limit labels carry the per-measure color; the (empty, not-yet-
        // shown) average label and the mean line use the neutral text color.
        Assert.All(ratePlot.Plot.GetPlottables<Text>().Where(t => t.LabelText.Length > 0),
            t => Assert.Equal(PlotColor.FromARGB(0xFFCC2233), t.LabelFontColor));
        Assert.All(amplitudePlot.Plot.GetPlottables<Text>().Where(t => t.LabelText.Length > 0),
            t => Assert.Equal(PlotColor.FromARGB(0xFF2266CC), t.LabelFontColor));
        Assert.Equal(PlotColor.FromARGB(0xFF404040), MeanLine(ratePlot).LineColor);
        Assert.Equal(PlotColor.FromARGB(0xFF404040), MeanLine(amplitudePlot).LineColor);
        Assert.Equal(PlotColor.FromARGB(0xFF404040), AvgLabel(ratePlot).LabelFontColor);
        Assert.Equal(PlotColor.FromARGB(0xFF404040), AvgLabel(amplitudePlot).LabelFontColor);
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

        Assert.Equal(new[] { "-4", "+6" }, VisibleTextLabels(ratePlot));
        Assert.Equal(new[] { "270", "315" }, VisibleTextLabels(amplitudePlot));
    }

    [Fact]
    public void AverageOverlay_ShowsMeanLineSigmaBandAndLabelWhenStatsValid()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        renderer.CreateGraphs();

        // Hidden until per-position stats exist.
        Assert.False(MeanLine(ratePlot).IsVisible);
        Assert.False(SigmaBand(ratePlot).IsVisible);
        Assert.False(MeanLine(amplitudePlot).IsVisible);
        Assert.False(SigmaBand(amplitudePlot).IsVisible);

        var frame = new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                Version = 1,
                Rate = Series(new[] { 0.0, 1.0, 2.0, 3.0 }, new[] { 1.0, 2.0, 3.0, 2.0 }),
                Amplitude = Series(new[] { 0.0, 1.0, 2.0, 3.0 }, new[] { 284.0, 286.0, 285.0, 285.0 }),
                RateValid = true,
                AmplitudeValid = true,
                RateStats = new StatsSummary(true, -2.0, 6.0, 2.0, 1.5, 50),
                AmplitudeStats = new StatsSummary(true, 270.0, 300.0, 285.0, 4.0, 50),
            },
        };
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(48000));

        // Mean line sits at the running mean; the ±σ band spans mean±sigma.
        Assert.True(MeanLine(ratePlot).IsVisible);
        Assert.Equal(2.0, MeanLine(ratePlot).Y);
        Assert.True(SigmaBand(ratePlot).IsVisible);
        Assert.Equal(0.5, SigmaBand(ratePlot).Y1, 6);
        Assert.Equal(3.5, SigmaBand(ratePlot).Y2, 6);

        Assert.True(MeanLine(amplitudePlot).IsVisible);
        Assert.Equal(285.0, MeanLine(amplitudePlot).Y);
        Assert.Equal(281.0, SigmaBand(amplitudePlot).Y1, 6);
        Assert.Equal(289.0, SigmaBand(amplitudePlot).Y2, 6);

        // The label on the line reads the average value and the deviation.
        string rateAvg = AvgLabelText(ratePlot);
        Assert.Contains("avg +2.0 s/d", rateAvg);
        Assert.Contains("σ 1.5", rateAvg);
        string amplitudeAvg = AvgLabelText(amplitudePlot);
        Assert.Contains("avg 285°", amplitudeAvg);
        Assert.Contains("σ 4.0", amplitudeAvg);

        foreach (Annotation avgLabel in new[] { AvgLabel(ratePlot), AvgLabel(amplitudePlot) })
        {
            Assert.True(avgLabel.IsVisible);
            Assert.Equal(Alignment.UpperLeft, avgLabel.Alignment);
            Assert.Equal(8.0f, avgLabel.OffsetX);
            Assert.Equal(6.0f, avgLabel.OffsetY);
        }

        AssertAverageReadoutInsideUpperLeft(ratePlot);
        AssertAverageReadoutInsideUpperLeft(amplitudePlot);
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

    [Fact]
    public void LiveFollow_PinsXAxisToDataExtent_SoTheTraceDoesNotShrink()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        renderer.CreateGraphs();

        // Drive a growing live trace: each frame appends one beat, advancing the
        // time axis by 1 s. The accept-range value labels are ScottPlot Text
        // plottables with no EnableAutoscale opt-out, so a plain per-frame AutoScale
        // folded each label (parked just past the margin-padded right edge) back
        // into the X fit and ratcheted the axis steadily past the data — the trace
        // shrank toward the left. Live-follow must keep the newest sample pinned to
        // the right edge.
        const int beats = 80;
        var x = new List<double>();
        var rateY = new List<double>();
        var ampY = new List<double>();
        for (int i = 0; i < beats; i++)
        {
            x.Add(i);
            rateY.Add(i % 2 == 0 ? 1.0 : 2.0);
            ampY.Add(i % 2 == 0 ? 281.0 : 282.0);

            renderer.RenderFrame(
                new AnalysisFrame
                {
                    MetricsHistory = new BeatMetricsHistorySnapshot
                    {
                        Version = (ulong)(i + 1),
                        Rate = Series(x.ToArray(), rateY.ToArray()),
                        Amplitude = Series(x.ToArray(), ampY.ToArray()),
                        RateValid = true,
                        AmplitudeValid = true,
                    },
                },
                new AnalysisTabRenderContext(48000));
        }

        double dataMin = 0.0;
        double dataMax = beats - 1;
        // Newest sample sits at the right edge and the first at the left edge on both
        // stacked panes: the trace fills the pane width with no accumulated empty
        // band on the right.
        Assert.Equal(dataMax, ratePlot.Plot.Axes.GetLimits().Right, 0.5);
        Assert.Equal(dataMax, amplitudePlot.Plot.Axes.GetLimits().Right, 0.5);
        Assert.Equal(dataMin, ratePlot.Plot.Axes.GetLimits().Left, 0.5);
        Assert.Equal(dataMin, amplitudePlot.Plot.Axes.GetLimits().Left, 0.5);
        // Both panes resolve to one shared time window (stacked-pane alignment).
        Assert.Equal(
            ratePlot.Plot.Axes.GetLimits().Right,
            amplitudePlot.Plot.Axes.GetLimits().Right,
            0.5);
    }

    [Fact]
    public void Smoothing_DefaultsOnAndCanBeToggledOffAndOn()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        renderer.CreateGraphs();

        Assert.Equal("CubicSpline", PathStrategy(ratePlot));
        Assert.Equal("CubicSpline", PathStrategy(amplitudePlot));

        renderer.SetSmoothing(false);
        Assert.Equal("Straight", PathStrategy(ratePlot));
        Assert.Equal("Straight", PathStrategy(amplitudePlot));

        renderer.SetSmoothing(true);
        Assert.Equal("CubicSpline", PathStrategy(ratePlot));
        Assert.Equal("CubicSpline", PathStrategy(amplitudePlot));
    }

    [Fact]
    public void Smoothing_SurvivesResetThatRebuildsTheScatters()
    {
        TraceDisplayRenderer renderer = NewRenderer(out AvaPlot ratePlot, out AvaPlot amplitudePlot);
        renderer.CreateGraphs();
        renderer.SetSmoothing(false);

        renderer.Reset();

        Assert.Equal("Straight", PathStrategy(ratePlot));
        Assert.Equal("Straight", PathStrategy(amplitudePlot));
    }

    private static Scatter Line(AvaPlot plot) => plot.Plot.GetPlottables<Scatter>().Single();

    private static string PathStrategy(AvaPlot plot) => Line(plot).PathStrategy.GetType().Name;

    // The accept band stays in autoscale; the ±σ deviation band opts out, so these
    // select each one specifically now that both plots carry two VerticalSpans.
    private static VerticalSpan Band(AvaPlot plot) =>
        plot.Plot.GetPlottables<VerticalSpan>().Single(s => s.EnableAutoscale);

    private static VerticalSpan SigmaBand(AvaPlot plot) =>
        plot.Plot.GetPlottables<VerticalSpan>().Single(s => !s.EnableAutoscale);

    private static HorizontalLine MeanLine(AvaPlot plot) =>
        plot.Plot.GetPlottables<HorizontalLine>().Single();

    private static Annotation AvgLabel(AvaPlot plot) =>
        plot.Plot.GetPlottables<Annotation>().Single();

    private static string AvgLabelText(AvaPlot plot) =>
        AvgLabel(plot).LabelText;

    private static void AssertAverageReadoutInsideUpperLeft(AvaPlot plot)
    {
        plot.Plot.RenderInMemory(900, 240);
        PixelRect dataRect = plot.Plot.RenderManager.LastRender.DataRect;
        PixelRect labelRect = AvgLabel(plot).LabelLastRenderPixelRect;

        Assert.InRange(labelRect.Left, dataRect.Left, dataRect.Left + 20);
        Assert.InRange(labelRect.Top, dataRect.Top, dataRect.Top + 20);
    }

    private static string[] VisibleTextLabels(AvaPlot plot) =>
        plot.Plot.GetPlottables<Text>()
            .Where(text => text.IsVisible)
            .Select(text => text.LabelText)
            .ToArray();
}
