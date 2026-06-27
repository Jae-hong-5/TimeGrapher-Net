using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class EscapementAnalyzerRendererTests
{
    private static EscapementAnalyzerRenderer NewRenderer(out AvaPlot plot)
    {
        plot = new AvaPlot();
        var valueTexts = new TextBlock[EscapementReadout.Labels.Length];
        for (int i = 0; i < valueTexts.Length; i++)
        {
            valueTexts[i] = new TextBlock();
        }

        var renderer = new EscapementAnalyzerRenderer(plot, valueTexts, "Arial");
        renderer.CreateGraphs();
        return renderer;
    }

    [Fact]
    public void CreateGraphsUsesCompactAxisPanels()
    {
        _ = NewRenderer(out AvaPlot plot);

        Assert.Equal(PlotThemeHelper.CompactLeftAxisSizePx, plot.Plot.Axes.Left.MinimumSize);
        Assert.Equal(PlotThemeHelper.CompactBottomAxisSizePx, plot.Plot.Axes.Bottom.MinimumSize);
    }

    [Fact]
    public void RenderFrameScalesRawScopeAroundZeroFromRawMinMaxInsteadOfEnvelope()
    {
        EscapementAnalyzerRenderer renderer = NewRenderer(out AvaPlot plot);

        var segment = new BeatSegment
        {
            Samples = new float[] { 9.0f, 0.1f, 0.1f, 0.1f },
            RawValid = true,
            RawMin = new float[] { 0.0f, -0.6f, 0.0f, 0.0f },
            RawMax = new float[] { 0.8f, 0.1f, 0.0f, 0.0f },
            MsPerPoint = 0.25,
            AOffsetMs = 0.0,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        AxisLimits limits = plot.Plot.Axes.GetLimits();
        Assert.InRange(limits.Top, 0.87, 0.89);
        Assert.InRange(limits.Bottom, -0.89, -0.87);

        HorizontalLine zeroLine = plot.Plot.GetPlottables<HorizontalLine>().Single();
        Assert.Equal(0.0, zeroLine.Y, 6);
        Assert.True(zeroLine.IsVisible);
    }

    [Fact]
    public void RenderFrameZoomsToEscapementWindowAndRezeroesAxisAtA()
    {
        EscapementAnalyzerRenderer renderer = NewRenderer(out AvaPlot plot);

        // A at 5 ms (the capture's pre-A roll), C peak 6.8 ms after it; the raw
        // window spans 20 ms — far less than the 400 ms capture window, but the
        // view must zoom to frame A→C regardless.
        const double msPerPoint = 0.25;
        var rawMin = new float[80];
        var rawMax = new float[80];
        rawMax[20] = 0.4f;  // A burst at 5.0 ms
        rawMin[20] = -0.4f;
        rawMax[47] = 1.0f;  // C burst at 11.75 ms (≈ A + 6.8 ms)
        rawMin[47] = -1.0f;

        var segment = new BeatSegment
        {
            Samples = new float[80],
            RawValid = true,
            RawMin = rawMin,
            RawMax = rawMax,
            MsPerPoint = msPerPoint,
            AOffsetMs = 5.0,
            CPeakValid = true,
            CPeakOffsetMs = 11.8,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        AxisLimits limits = plot.Plot.Axes.GetLimits();
        // X is A-relative with asymmetric padding. Content runs -5..6.8 ms
        // (span 11.8), below both pad floors: left = -5 - 6, right = 6.8 + 12.
        Assert.InRange(limits.Left, -11.01, -10.99);
        Assert.InRange(limits.Right, 18.79, 18.81);
        // Not the unzoomed 400 ms capture window.
        Assert.True(limits.Right < BeatSegmentCapture.WindowMs);

        // The visible marker lines are re-zeroed at A: A at 0, C peak at the A→C
        // interval (6.8 ms) — the same value its label carries.
        double[] markerXs = plot.Plot.GetPlottables()
            .OfType<VerticalLine>()
            .Where(line => line.IsVisible)
            .Select(line => line.X)
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(2, markerXs.Length);
        Assert.InRange(markerXs[0], -0.01, 0.01);
        Assert.InRange(markerXs[1], 6.79, 6.81);

        Assert.DoesNotContain(
            plot.Plot.GetPlottables<Text>().Where(text => text.IsVisible),
            text => text.LabelText.StartsWith("tic A-toc A", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderFrameShowsTicTocPairAnchoredOnTicPhase()
    {
        EscapementAnalyzerRenderer renderer = NewRenderer(out AvaPlot plot);

        const double msPerPoint = 0.25;
        // Tic anchor window: 150 ms of raw, long enough to span the toc that
        // follows ~120 ms later. Spikes at the tic A/C and (for realism) the toc.
        var rawMin = new float[600];
        var rawMax = new float[600];
        rawMax[20] = 0.4f; rawMin[20] = -0.4f;      // tic A at 5.0 ms
        rawMax[47] = 1.0f; rawMin[47] = -1.0f;      // tic C at 11.75 ms (A + 6.8)
        rawMax[500] = 0.8f; rawMin[500] = -0.8f;    // toc burst region at 125 ms

        var tic = new BeatSegment
        {
            Samples = new float[600],
            RawValid = true,
            RawMin = rawMin,
            RawMax = rawMax,
            MsPerPoint = msPerPoint,
            StartTimeS = 0.0,
            IsTic = true,
            AOffsetMs = 5.0,
            CPeakValid = true,
            CPeakOffsetMs = 11.8,
        };
        var toc = new BeatSegment
        {
            Samples = Array.Empty<float>(),  // toc waveform is read from the tic window
            MsPerPoint = msPerPoint,
            StartTimeS = 0.12,               // 120 ms after the tic window start
            IsTic = false,
            AOffsetMs = 5.0,
            CPeakValid = true,
            CPeakOffsetMs = 11.9,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { tic, toc },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        AxisLimits limits = plot.Plot.Axes.GetLimits();
        Assert.InRange(limits.Left, -18.20, -18.18);
        Assert.InRange(limits.Right, 153.27, 153.29);
        Assert.True(limits.Right > 120.0);  // the toc beat is on screen, not just the tic

        double[] markerXs = plot.Plot.GetPlottables()
            .OfType<VerticalLine>()
            .Where(line => line.IsVisible)
            .Select(line => line.X)
            .OrderBy(x => x)
            .ToArray();
        // tic A (0), tic C peak (6.8), toc A (120), toc C peak (126.9); onsets absent.
        Assert.Equal(4, markerXs.Length);
        Assert.InRange(markerXs[0], -0.01, 0.01);
        Assert.InRange(markerXs[1], 6.79, 6.81);
        Assert.InRange(markerXs[2], 119.99, 120.01);
        Assert.InRange(markerXs[3], 126.89, 126.91);

        LinePlot connector = plot.Plot.GetPlottables<LinePlot>().Single();
        Assert.True(connector.IsVisible);

        Text connectorLabel = plot.Plot.GetPlottables<Text>()
            .Single(text => text.LabelText.StartsWith("tic A-toc A", StringComparison.Ordinal));
        Assert.True(connectorLabel.IsVisible);
        Assert.Equal("tic A-toc A 120.00 ms", connectorLabel.LabelText);
    }

    [Fact]
    public void SelectPairKeepsTicFirstWhenNewestBeatIsATic()
    {
        // Three alternating beats ending on a tic. "Latest two" would be
        // (toc, tic) — lead phase flipped — so the renderer must instead show the
        // last complete tic→toc pair (beats 0 and 1), keeping tic on the left.
        EscapementAnalyzerRenderer renderer = NewRenderer(out AvaPlot plot);

        BeatSegment Beat(double startS, bool isTic) => new()
        {
            Samples = new float[8],
            MsPerPoint = 0.25,
            StartTimeS = startS,
            IsTic = isTic,
            AOffsetMs = 5.0,
            CPeakValid = true,
            CPeakOffsetMs = 11.8,
        };

        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { Beat(0.00, true), Beat(0.12, false), Beat(0.24, true) },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        // Two beats shown (tic at 0, toc at ~120 ms) — the newest tic is held back
        // until its toc completes, so the order never becomes toc-first.
        double[] markerXs = plot.Plot.GetPlottables()
            .OfType<VerticalLine>()
            .Where(line => line.IsVisible)
            .Select(line => line.X)
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(4, markerXs.Length);
        Assert.InRange(markerXs[0], -0.01, 0.01);     // tic A (beat 0)
        Assert.InRange(markerXs[2], 119.99, 120.01);  // toc A (beat 1), not the newest tic

        Text connectorLabel = plot.Plot.GetPlottables<Text>()
            .Single(text => text.LabelText.StartsWith("tic A-toc A", StringComparison.Ordinal));
        Assert.Equal("tic A-toc A 120.00 ms", connectorLabel.LabelText);
    }
}
