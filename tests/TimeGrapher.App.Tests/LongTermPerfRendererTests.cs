using System.Reflection;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;
using PlotColor = ScottPlot.Color;

namespace TimeGrapher.App.Tests;

public sealed class LongTermPerfRendererTests
{
    [Fact]
    public void RenderFrame_LabelsAcceptableRangeLimitsAndKeepsChrome()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(SampleFrame(), new AnalysisTabRenderContext(48000));

        ratePlot.Plot.RenderInMemory(900, 220);
        amplitudePlot.Plot.RenderInMemory(900, 220);
        beatErrorPlot.Plot.RenderInMemory(900, 220);

        Assert.False(ratePlot.Plot.Axes.Right.TickLabelStyle.IsVisible);
        Assert.False(amplitudePlot.Plot.Axes.Right.TickLabelStyle.IsVisible);
        Assert.False(beatErrorPlot.Plot.Axes.Right.TickLabelStyle.IsVisible);
        Assert.Equal(new[] { "-4", "+6" }, VisibleAcceptTextLabels(ratePlot));
        Assert.Equal(new[] { "270", "315" }, VisibleAcceptTextLabels(amplitudePlot));
        Assert.Equal(new[] { "-0.8", "+0.8" }, VisibleAcceptTextLabels(beatErrorPlot));
        Assert.Empty(AcceptLineLabels(ratePlot));
        Assert.Empty(AcceptLineLabels(amplitudePlot));
        Assert.Empty(AcceptLineLabels(beatErrorPlot));
        AssertUsesXOnlyGraphInput(ratePlot);
        AssertUsesXOnlyGraphInput(amplitudePlot);
        AssertUsesXOnlyGraphInput(beatErrorPlot);
        AssertUpperPaneHidesXAxis(ratePlot);
        AssertUpperPaneHidesXAxis(amplitudePlot);
    }

    [Fact]
    public void ApplyAcceptBands_MovesBandsAndLabelsLiveWithoutClearingHistory()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(SampleFrame(), new AnalysisTabRenderContext(48000));
        ratePlot.Plot.RenderInMemory(900, 220);
        Assert.Equal(new[] { "-4", "+6" }, VisibleAcceptTextLabels(ratePlot));

        AcceptBandSettings original = AcceptBandSettings.Current;
        try
        {
            AcceptBandSettings.Current = new AcceptBandSettings(-7.0, 5.0, 250.0, 310.0, 1.2);
            renderer.ApplyAcceptBands();
            ratePlot.Plot.RenderInMemory(900, 220);
            amplitudePlot.Plot.RenderInMemory(900, 220);
            beatErrorPlot.Plot.RenderInMemory(900, 220);

            // Bands and limit labels track the edited values, live, with no CreateGraphs.
            Assert.Equal(new[] { "-7", "+5" }, VisibleAcceptTextLabels(ratePlot));
            Assert.Equal(new[] { "250", "310" }, VisibleAcceptTextLabels(amplitudePlot));
            Assert.Equal(new[] { "-1.2", "+1.2" }, VisibleAcceptTextLabels(beatErrorPlot));
            // History is retained: the plotted reading is still in view after the edit.
            AssertIncludes(ratePlot, 1.8, 2.0);
            AssertIncludes(amplitudePlot, 282.0, 282.0);
        }
        finally
        {
            AcceptBandSettings.Current = original;
        }
    }

    [Fact]
    public void CreateGraphs_HidesAcceptLimitLabelsUntilFirstBeat()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        ratePlot.Plot.RenderInMemory(900, 220);
        amplitudePlot.Plot.RenderInMemory(900, 220);
        beatErrorPlot.Plot.RenderInMemory(900, 220);

        // Before any beat the value labels stay hidden (they would float at the
        // right edge of the empty placeholder window); the shaded band still shows.
        Assert.Empty(VisibleAcceptTextLabels(ratePlot));
        Assert.Empty(VisibleAcceptTextLabels(amplitudePlot));
        Assert.Empty(VisibleAcceptTextLabels(beatErrorPlot));
        Assert.True(AcceptBand(ratePlot).IsVisible);
        Assert.True(AcceptBand(amplitudePlot).IsVisible);
    }

    [Fact]
    public void CreateGraphs_UsesFullSummaryLabelsAndInitialDayWindow()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var verdict = new TextBlock();
        var rate = new TextBlock();
        var amplitude = new TextBlock();
        var beatError = new TextBlock();
        var renderer = new LongTermPerfRenderer(
            ratePlot,
            amplitudePlot,
            beatErrorPlot,
            new LongTermSummaryControls(verdict, rate, amplitude, beatError));

        renderer.CreateGraphs();

        Assert.Equal("Amplitude —", amplitude.Text);
        Assert.Equal("BEAT ERROR —", beatError.Text);
        AssertInitialDayWindow(ratePlot);
        AssertInitialDayWindow(amplitudePlot);
        AssertInitialDayWindow(beatErrorPlot);
    }

    [Fact]
    public void ApplyTheme_ColorsAcceptableRangeBandAndLabelsByMeasure()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);
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

        AssertTraceColor(ratePlot, palette.VarioBad);
        AssertTraceColor(amplitudePlot, palette.VarioMinMax);
        AssertTraceColor(beatErrorPlot, palette.TraceTick);
        AssertAcceptLabelColor(ratePlot, palette.VarioBad);
        AssertAcceptBandColor(ratePlot, palette.VarioBad);
        AssertAcceptLabelColor(amplitudePlot, palette.VarioMinMax);
        AssertAcceptBandColor(amplitudePlot, palette.VarioMinMax);
        AssertAcceptLabelColor(beatErrorPlot, palette.TraceTick);
        AssertAcceptBandColor(beatErrorPlot, palette.TraceTick);
        // FigureBackground now inherits SurfaceBg (the ScopeBg override was removed).
        foreach (AvaPlot plot in new[] { ratePlot, amplitudePlot, beatErrorPlot })
        {
            Assert.Equal(PlotColor.FromARGB(palette.SurfaceBg), plot.Plot.FigureBackground.Color);
        }
    }

    [Fact]
    public void RenderFrame_UpdatesCompactSummary()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var verdict = new TextBlock();
        var rate = new TextBlock();
        var amplitude = new TextBlock();
        var beatError = new TextBlock();
        var renderer = new LongTermPerfRenderer(
            ratePlot,
            amplitudePlot,
            beatErrorPlot,
            new LongTermSummaryControls(verdict, rate, amplitude, beatError));

        renderer.CreateGraphs();
        renderer.RenderFrame(SampleFrame(), new AnalysisTabRenderContext(48000, ReviewCursorTimeS: 3720.0));

        Assert.Equal("IN TOLERANCE", verdict.Text);
        Assert.Equal("Error Rate +1.8 s/d", rate.Text);
        Assert.Equal("Amplitude 282°", amplitude.Text);
        Assert.Equal("BEAT ERROR +0.3 ms", beatError.Text);
    }

    [Fact]
    public void TimeWindowNavigation_ReautoscalesYToDataAndWholeAcceptBand()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(SampleFrame(), new AnalysisTabRenderContext(48000));
        ratePlot.Plot.Axes.SetLimitsY(100, 110);
        amplitudePlot.Plot.Axes.SetLimitsY(100, 110);
        beatErrorPlot.Plot.Axes.SetLimitsY(100, 110);

        renderer.ShowTimeWindow(60 * 60);

        AssertIncludes(ratePlot, -4, 6);
        AssertIncludes(ratePlot, 1.8, 2.0);
        AssertIncludes(amplitudePlot, 270, 315);
        AssertIncludes(amplitudePlot, 282.0, 282.0);
        AssertIncludes(beatErrorPlot, -0.8, 0.8);
        AssertIncludes(beatErrorPlot, 0.3, 0.3);
        Assert.True(AcceptBand(amplitudePlot).IsVisible);
        Assert.True(AcceptBand(beatErrorPlot).IsVisible);
        Assert.Equal(new[] { "270", "315" }, VisibleAcceptTextLabels(amplitudePlot));
    }

    [Fact]
    public void TimeWindowNavigation_PullsNearbyToleranceLimitIntoView()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(NearAmplitudeLimitFrame(), new AnalysisTabRenderContext(48000));

        renderer.ShowTimeWindow(60 * 60);

        // The amplitude accept band is always pulled into view, so its limits and
        // labels stay visible alongside the trace (the hybrid corridor inclusion).
        AssertIncludes(amplitudePlot, 298.5, 300.0);
        Assert.True(AcceptBand(amplitudePlot).IsVisible);
        Assert.Equal(new[] { "270", "315" }, VisibleAcceptTextLabels(amplitudePlot));
    }

    [Fact]
    public void TimeWindowNavigation_ShowsVisibleAcceptBandWhenAmplitudeRunsAboveLimit()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(AboveAmplitudeLimitFrame(), new AnalysisTabRenderContext(48000));

        renderer.ShowTimeWindow(60 * 60);

        var limits = amplitudePlot.Plot.Axes.GetLimits();
        Assert.True(AcceptBand(amplitudePlot).IsVisible);
        Assert.True(limits.Bottom <= LongTermAcceptPolicy.Amplitude.Min);
        Assert.True(limits.Top >= 305.0);
        Assert.Equal(new[] { "270", "315" }, VisibleAcceptTextLabels(amplitudePlot));
    }

    [Fact]
    public void UserXAxisPan_ClampsToFirstDataPoint()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(SampleFrame(), new AnalysisTabRenderContext(48000));

        ratePlot.Plot.Axes.SetLimitsX(-1800.0, 1800.0);
        SyncXAxisFrom(renderer);

        AssertXWindow(ratePlot, 0.0, 3600.0);
        AssertXWindow(amplitudePlot, 0.0, 3600.0);
        AssertXWindow(beatErrorPlot, 0.0, 3600.0);
    }

    [Fact]
    public void UserXAxisPanToLatest_ReenablesLiveFollow()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithRange(1, 7200.0), new AnalysisTabRenderContext(48000));

        ratePlot.Plot.Axes.SetLimitsX(0.0, 3600.0);
        SyncXAxisFrom(renderer);
        renderer.RenderFrame(FrameWithRange(2, 10800.0), new AnalysisTabRenderContext(48000));
        AssertXWindow(ratePlot, 0.0, 3600.0);

        ratePlot.Plot.Axes.SetLimitsX(7200.0, 10800.0);
        SyncXAxisFrom(renderer);
        renderer.RenderFrame(FrameWithRange(3, 14400.0), new AnalysisTabRenderContext(48000));

        AssertXWindow(ratePlot, 10800.0, 14400.0);
        AssertXWindow(amplitudePlot, 10800.0, 14400.0);
        AssertXWindow(beatErrorPlot, 10800.0, 14400.0);
    }

    [Fact]
    public void PanRightToLatest_ReenablesLiveFollow()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithRange(1, 7200.0), new AnalysisTabRenderContext(48000));

        ratePlot.Plot.Axes.SetLimitsX(0.0, 3600.0);
        SyncXAxisFrom(renderer);
        renderer.PanRight();
        renderer.PanRight();
        renderer.PanRight();
        renderer.PanRight();
        renderer.RenderFrame(FrameWithRange(2, 10800.0), new AnalysisTabRenderContext(48000));

        AssertXWindow(ratePlot, 7200.0, 10800.0);
        AssertXWindow(amplitudePlot, 7200.0, 10800.0);
        AssertXWindow(beatErrorPlot, 7200.0, 10800.0);
    }

    [Fact]
    public void WheelZoom_ZoomsSharedXWindowWithoutMutatingY()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithRange(1, 7200.0), new AnalysisTabRenderContext(48000));
        AxisLimits beforeRate = ratePlot.Plot.Axes.GetLimits();
        AxisLimits beforeAmplitude = amplitudePlot.Plot.Axes.GetLimits();
        AxisLimits beforeBeatError = beatErrorPlot.Plot.Axes.GetLimits();

        WheelZoom(renderer, source: 0, deltaY: 1.0);

        AxisLimits afterRate = ratePlot.Plot.Axes.GetLimits();
        Assert.True(afterRate.Right - afterRate.Left < beforeRate.Right - beforeRate.Left);
        AssertXWindow(amplitudePlot, afterRate.Left, afterRate.Right);
        AssertXWindow(beatErrorPlot, afterRate.Left, afterRate.Right);
        Assert.Equal(beforeRate.Bottom, afterRate.Bottom, 10);
        Assert.Equal(beforeRate.Top, afterRate.Top, 10);
        Assert.Equal(beforeAmplitude.Bottom, amplitudePlot.Plot.Axes.GetLimits().Bottom, 10);
        Assert.Equal(beforeAmplitude.Top, amplitudePlot.Plot.Axes.GetLimits().Top, 10);
        Assert.Equal(beforeBeatError.Bottom, beatErrorPlot.Plot.Axes.GetLimits().Bottom, 10);
        Assert.Equal(beforeBeatError.Top, beatErrorPlot.Plot.Axes.GetLimits().Top, 10);
    }

    [Fact]
    public void ZoomIn_StopsAtTenSecondXWindow()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithRange(1, 120.0), new AnalysisTabRenderContext(48000));

        for (int i = 0; i < 8; i++)
        {
            renderer.ZoomIn();
        }

        AssertXWindow(ratePlot, 55.0, 65.0);
        AssertXWindow(amplitudePlot, 55.0, 65.0);
        AssertXWindow(beatErrorPlot, 55.0, 65.0);
    }

    [Fact]
    public void WheelZoom_StopsAtTenSecondXWindow()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithRange(1, 120.0), new AnalysisTabRenderContext(48000));

        for (int i = 0; i < 8; i++)
        {
            WheelZoom(renderer, source: 0, deltaY: 1.0);
        }

        AssertXWindow(ratePlot, 55.0, 65.0);
        AssertXWindow(amplitudePlot, 55.0, 65.0);
        AssertXWindow(beatErrorPlot, 55.0, 65.0);
    }

    [Fact]
    public void ZoomIn_UsesFullDataRangeWhenHistoryIsShorterThanTenSeconds()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithRange(1, 6.0), new AnalysisTabRenderContext(48000));

        renderer.ZoomIn();

        AssertXWindow(ratePlot, 0.0, 6.0);
        AssertXWindow(amplitudePlot, 0.0, 6.0);
        AssertXWindow(beatErrorPlot, 0.0, 6.0);
    }

    [Fact]
    public void RenderFrame_LeavesPositionMarkerBackgroundTransparent()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        renderer.RenderFrame(FrameWithPositionChange(), new AnalysisTabRenderContext(48000));

        foreach (AvaPlot plot in new[] { ratePlot, amplitudePlot, beatErrorPlot })
        {
            VerticalLine marker = plot.Plot.GetPlottables<VerticalLine>()
                .Single(line => line.LabelText == "CH");

            Assert.Equal(ScottPlot.Colors.Transparent, marker.LabelBackgroundColor);
        }
    }

    [Fact]
    public void CreateGraphs_AppliesReadableElapsedTicksToAllPanes()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        ratePlot.Plot.RenderInMemory(900, 220);
        amplitudePlot.Plot.RenderInMemory(900, 220);
        beatErrorPlot.Plot.RenderInMemory(900, 220);

        string[] expected = { "00:00", "06:00", "12:00", "18:00", "24:00" };
        Assert.Equal(expected, BottomTickLabels(ratePlot));
        Assert.Equal(expected, BottomTickLabels(amplitudePlot));
        Assert.Equal(expected, BottomTickLabels(beatErrorPlot));
    }

    [Fact]
    public void CreateGraphs_ReservesBottomGapOnUpperPanesSoLabelsAreNotClipped()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();
        amplitudePlot.Plot.RenderInMemory(900, 220);

        var render = amplitudePlot.Plot.RenderManager.LastRender;
        Assert.True(
            render.FigureRect.Bottom - render.DataRect.Bottom >= 10f,
            $"expected a reserved bottom gap, got {render.FigureRect.Bottom - render.DataRect.Bottom}");
    }

    [Fact]
    public void CreateGraphs_PinsLeftPanelOnEveryPane()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();

        foreach (AvaPlot plot in new[] { ratePlot, amplitudePlot, beatErrorPlot })
        {
            Assert.Equal(60f, plot.Plot.Axes.Left.MinimumSize);
            Assert.Equal(60f, plot.Plot.Axes.Left.MaximumSize);
        }
    }

    [Fact]
    public void DataArea_StaysConstantWidthAndAlignedAcrossLabelGrowthAndControlSize()
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);
        renderer.CreateGraphs();

        // Wide labels (signed rate, wide amplitude swing, sub-unit beat error) and a
        // maximized control: the pinned 60 px left panel must hold on every pane.
        renderer.RenderFrame(WideLabelFrame(), new AnalysisTabRenderContext(48000));

        float rate = LeftPanel(ratePlot, 900, 240);
        float amplitude = LeftPanel(amplitudePlot, 900, 240);
        float beatError = LeftPanel(beatErrorPlot, 900, 240);
        float beatErrorBig = LeftPanel(beatErrorPlot, 1900, 980);

        Assert.Equal(60f, rate, 1f);
        Assert.Equal(60f, amplitude, 1f);
        Assert.Equal(60f, beatError, 1f);
        // Equal across panes (left edges aligned) and constant across control size.
        Assert.Equal(rate, amplitude, 1f);
        Assert.Equal(amplitude, beatError, 1f);
        Assert.Equal(beatError, beatErrorBig, 1f);
    }

    private static float LeftPanel(AvaPlot plot, int width, int height)
    {
        plot.Plot.RenderInMemory(width, height);
        var render = plot.Plot.RenderManager.LastRender;
        return render.DataRect.Left - render.FigureRect.Left;
    }

    private static AnalysisFrame WideLabelFrame()
    {
        double[] x = { 0.0, 3600.0, 7200.0 };
        return new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                Version = 1,
                Rate = Series(x, new[] { -15.0, 14.0, -11.0 }),
                Amplitude = Series(x, new[] { 268.0, 311.0, 304.5 }),
                BeatError = Series(x, new[] { -0.7, 0.65, -0.55 }),
                RateValid = true,
                AmplitudeValid = true,
                BeatErrorValid = true,
                Bph = 21600,
                LatestTimeS = 7200.0,
            },
        };
    }

    private static string[] AcceptLineLabels(AvaPlot plot) =>
        plot.Plot.GetPlottables<HorizontalLine>()
            .Where(line => !string.IsNullOrWhiteSpace(line.LabelText))
            .Select(line => line.LabelText)
            .ToArray();

    private static string[] VisibleAcceptTextLabels(AvaPlot plot) =>
        plot.Plot.GetPlottables<Text>()
            .Where(text => text.IsVisible)
            .Select(text => text.LabelText)
            .ToArray();

    private static void AssertTraceColor(AvaPlot plot, uint expected)
    {
        Scatter line = plot.Plot.GetPlottables<Scatter>().Single();

        Assert.Equal(PlotColor.FromARGB(expected), line.LineColor);
    }

    private static void AssertAcceptLabelColor(AvaPlot plot, uint expected)
    {
        PlotColor color = PlotColor.FromARGB(expected);
        Text[] labels = plot.Plot.GetPlottables<Text>().ToArray();

        // Both the min and max limit labels must carry the color, not just one.
        Assert.Equal(2, labels.Length);
        Assert.All(labels, text => Assert.Equal(color, text.LabelFontColor));
    }

    private static void AssertUsesXOnlyGraphInput(AvaPlot plot)
    {
        MouseDragPan pan = plot.UserInputProcessor.UserActionResponses
            .OfType<MouseDragPan>()
            .Single();

        Assert.True(pan.LockY);
        Assert.False(pan.LockX);
        Assert.DoesNotContain(plot.UserInputProcessor.UserActionResponses, response =>
            response is MouseWheelZoom or MouseDragZoom or MouseDragZoomRectangle);
    }

    private static void AssertAcceptBandColor(AvaPlot plot, uint expected)
    {
        VerticalSpan span = AcceptBand(plot);
        PlotColor color = PlotColor.FromARGB(expected).WithAlpha(42);

        Assert.Empty(plot.Plot.GetPlottables<HorizontalSpan>());
        Assert.Equal(color, span.FillStyle.Color);
        Assert.Equal(0, span.LineStyle.Width);
    }

    private static VerticalSpan AcceptBand(AvaPlot plot) =>
        plot.Plot.GetPlottables<VerticalSpan>().Single();

    private static string[] BottomTickLabels(AvaPlot plot) =>
        plot.Plot.Axes.Bottom.TickGenerator.Ticks
            .Select(tick => tick.Label)
            .ToArray();

    private static void AssertInitialDayWindow(AvaPlot plot)
    {
        var limits = plot.Plot.Axes.GetLimits();

        Assert.Equal(0.0, limits.Left);
        Assert.Equal(24 * 60 * 60, limits.Right);
    }

    private static void AssertXWindow(AvaPlot plot, double left, double right)
    {
        var limits = plot.Plot.Axes.GetLimits();

        Assert.Equal(left, limits.Left, 6);
        Assert.Equal(right, limits.Right, 6);
    }

    private static void AssertIncludes(AvaPlot plot, double min, double max)
    {
        var limits = plot.Plot.Axes.GetLimits();

        Assert.True(limits.Bottom <= min, $"{limits.Bottom} should include {min}");
        Assert.True(limits.Top >= max, $"{limits.Top} should include {max}");
    }

    private static void AssertUpperPaneHidesXAxis(AvaPlot plot)
    {
        Assert.False(plot.Plot.Axes.Bottom.IsVisible);
        Assert.False(plot.Plot.Axes.Bottom.TickLabelStyle.IsVisible);
        Assert.Equal(0, plot.Plot.Axes.Bottom.MajorTickStyle.Length);
        Assert.Equal(0, plot.Plot.Axes.Bottom.MinorTickStyle.Length);
        // A small bottom panel is reserved so the lowest left-axis tick label is
        // not clipped against the pane edge, even with the X axis hidden.
        Assert.Equal(10f, plot.Plot.Axes.Bottom.MinimumSize);
    }

    private static AnalysisFrame SampleFrame() => new()
    {
        MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = 1,
            Rate = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 1.0, 2.0, 1.8 }),
            Amplitude = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 281.0, 282.0, 282.0 }),
            BeatError = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 0.2, 0.3, 0.3 }),
            RateValid = true,
            RateSPerDay = 1.8,
            AmplitudeValid = true,
            AmplitudeDeg = 282.0,
            BeatErrorValid = true,
            BeatErrorSignedMs = 0.3,
            Bph = 21600,
            LatestTimeS = 18 * 3600 + 42 * 60 + 10,
        },
    };

    private static AnalysisFrame NearAmplitudeLimitFrame() => new()
    {
        MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = 1,
            Rate = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 1.0, 2.0, 1.8 }),
            Amplitude = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 298.0, 299.0, 298.5 }),
            BeatError = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 0.2, 0.3, 0.3 }),
            AmplitudeValid = true,
            AmplitudeDeg = 298.5,
            Bph = 21600,
            LatestTimeS = 7200.0,
        },
    };

    private static AnalysisFrame AboveAmplitudeLimitFrame() => new()
    {
        MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = 1,
            Rate = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 1.0, 2.0, 1.8 }),
            Amplitude = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { 303.5, 305.0, 304.2 }),
            BeatError = Series(new[] { 0.0, 3600.0, 7200.0 }, new[] { -0.01, 0.02, -0.01 }),
            AmplitudeValid = true,
            AmplitudeDeg = 304.2,
            Bph = 21600,
            LatestTimeS = 7200.0,
        },
    };

    private static AnalysisFrame FrameWithRange(ulong version, double maxX)
    {
        double[] x = new[] { 0.0, maxX / 2.0, maxX };

        return new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                Version = version,
                Rate = Series(x, new[] { 1.0, 1.5, 1.8 }),
                Amplitude = Series(x, new[] { 281.0, 282.0, 282.0 }),
                BeatError = Series(x, new[] { 0.2, 0.3, 0.3 }),
                RateValid = true,
                RateSPerDay = 1.8,
                AmplitudeValid = true,
                AmplitudeDeg = 282.0,
                BeatErrorValid = true,
                BeatErrorSignedMs = 0.3,
                Bph = 21600,
                LatestTimeS = maxX,
            },
        };
    }


    private static AnalysisFrame FrameWithPositionChange()
    {
        AnalysisFrame frame = SampleFrame();
        BeatMetricsHistorySnapshot source = frame.MetricsHistory!;

        frame.MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = source.Version,
            Rate = source.Rate,
            Amplitude = source.Amplitude,
            BeatError = source.BeatError,
            RateValid = source.RateValid,
            RateSPerDay = source.RateSPerDay,
            AmplitudeValid = source.AmplitudeValid,
            AmplitudeDeg = source.AmplitudeDeg,
            BeatErrorValid = source.BeatErrorValid,
            BeatErrorSignedMs = source.BeatErrorSignedMs,
            Bph = source.Bph,
            LatestTimeS = source.LatestTimeS,
            PositionChanges = new[] { new PositionChange(0.0, WatchPosition.CH) },
        };

        return frame;
    }

    private static MetricsHistorySeries Series(double[] x, double[] y) => new()
    {
        X = x,
        Y = y,
        YMin = y,
        YMax = y,
    };

    private static void SyncXAxisFrom(LongTermPerfRenderer renderer)
    {
        var method = typeof(LongTermPerfRenderer).GetMethod(
            "SyncXAxisFrom",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(renderer, new object[] { 0 });
    }

    private static void WheelZoom(LongTermPerfRenderer renderer, int source, double deltaY)
    {
        var method = typeof(LongTermPerfRenderer).GetMethod(
            "OnWheelZoom",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(renderer, new object[] { source, deltaY });
    }
}
