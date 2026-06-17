using Avalonia.Controls;
using ScottPlot.Avalonia;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the Escapement Analyzer observe-path contract: the repeatability
/// tracker accumulates from every routed frame via the consumer's
/// ObserveFrame — also while another tab is active and RenderFrame never
/// runs — and the version-gated catch-up re-feed inside RenderFrame does not
/// double-count a snapshot the observe path already consumed.
/// </summary>
public sealed class EscapementAnalyzerObservePathTests
{
    private static BeatSegment Segment(double startTimeS) => new()
    {
        Samples = new float[] { 0.1f, 1.0f, 0.4f, 0.2f },
        MsPerPoint = 0.25,
        StartTimeS = startTimeS,
        AOffsetMs = 5.0,
        CPeakValid = true,
        CPeakOffsetMs = 150.0,
    };

    private static AnalysisFrame Frame(ulong version, params BeatSegment[] segments) => new()
    {
        BeatSegments = new BeatSegmentsSnapshot { Version = version, Segments = segments },
    };

    [Fact]
    public void ObserveFrameFeedsTheTrackerWhileInactiveWithoutDoubleCountingOnRender()
    {
        var valueTexts = new TextBlock[EscapementReadout.Labels.Length];
        for (int i = 0; i < valueTexts.Length; i++)
        {
            valueTexts[i] = new TextBlock();
        }

        var renderer = new EscapementAnalyzerRenderer(new AvaPlot(), valueTexts, "Arial");
        var consumer = new EscapementAnalyzerFrameConsumer(renderer);
        consumer.Initialize(new AnalysisTabResetContext(
            SampleRate: 48000, RateErrorYScale: 1.0, RateDataPoints: 600));

        // Two beats arrive while another tab is active: this consumer only
        // ever sees them on the observe path.
        consumer.ObserveFrame(Frame(version: 1, Segment(0.00), Segment(0.25)));

        // By the time this tab renders again, the bounded capture ring has
        // rotated the older segments out: the latest snapshot holds one beat.
        AnalysisFrame latest = Frame(version: 2, Segment(0.50));
        consumer.ObserveFrame(latest);

        int meanSigmaIndex = Array.IndexOf(EscapementReadout.Labels, "PEAK MEAN±σ");
        // The observe path only accumulates; it must not render the readout.
        Assert.Equal(VarioReadout.Missing, valueTexts[meanSigmaIndex].Text);

        consumer.RenderFrame(latest, new AnalysisTabRenderContext(SampleRate: 48000));

        // n=3 needs the two observe-only beats (a render-only feed of the
        // latest snapshot would read n=1), and the catch-up re-feed of the
        // same snapshot version inside RenderFrame must not double-count.
        Assert.Equal("145.0 ±0.00 ms (n=3)", valueTexts[meanSigmaIndex].Text);
    }
}
