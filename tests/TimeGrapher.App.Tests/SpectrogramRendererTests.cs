using System.Reflection;

using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SpectrogramRendererTests
{
    // The time-axis caption is renderer-filled state; with no frame published yet
    // _lastImage stays null, so RenderCurrent updates the axis labels/caption and
    // returns before any bitmap blit (which would need the Avalonia platform). That
    // lets the view-mode labeling be asserted off-platform with dummy controls.
    private static SpectrogramRenderer CreateRenderer(TextBlock caption, TextBlock[] timeLabels) => new(
        new Image(),
        new Image(),
        new[] { new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock() },
        timeLabels,
        new Border(),
        new Border(),
        caption,
        new Border(),
        new Canvas(),
        System.Array.Empty<Avalonia.Controls.Shapes.Rectangle>(),
        System.Array.Empty<TextBlock>(),
        new Border(),
        System.Array.Empty<Avalonia.Controls.Control>());

    private static TextBlock[] TimeLabels() =>
        new[] { new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock() };

    // NOTE: these three were rewritten after commit 28677d6 (per-beat 0-based
    // ruler + edge-to-edge direction strip) changed Beats-mode behavior — the
    // caption is now just the unit, and the window span is shown by the beat
    // ruler instead of the seconds time labels. The assertions now match that
    // committed behavior; the 28677d6 author should confirm the intended
    // beat-ruler coverage (clamp magnitude, per-beat labels) is what they want.
    [Fact]
    public void BeatsModeCaptionShowsTheTimeUnit()
    {
        var caption = new TextBlock();
        var renderer = CreateRenderer(caption, TimeLabels());

        renderer.SetViewMode(SpectrogramViewMode.Beats);
        Assert.Equal("Time (ms)", caption.Text);

        renderer.SetViewBeats(8);
        Assert.Equal("Time (ms)", caption.Text);
    }

    [Fact]
    public void BeatsModeShowsTheBeatRulerAndHidesTheSecondsTimeAxis()
    {
        var timeLabelGrid = new Border();
        var beatRulerCanvas = new Canvas();
        var renderer = new SpectrogramRenderer(
            new Image(),
            new Image(),
            new[] { new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock() },
            TimeLabels(),
            new Border(),
            timeLabelGrid,
            new TextBlock(),
            new Border(),
            beatRulerCanvas,
            System.Array.Empty<Avalonia.Controls.Shapes.Rectangle>(),
            System.Array.Empty<TextBlock>(),
            new Border(),
            System.Array.Empty<Avalonia.Controls.Control>());

        renderer.SetViewMode(SpectrogramViewMode.Seconds);
        Assert.True(timeLabelGrid.IsVisible);
        Assert.False(beatRulerCanvas.IsVisible);

        renderer.SetViewMode(SpectrogramViewMode.Beats);
        Assert.False(timeLabelGrid.IsVisible);
        Assert.True(beatRulerCanvas.IsVisible);
    }

    [Fact]
    public void SetViewBeatsBelowTwoClampsToTwo()
    {
        var caption = new TextBlock();
        var renderer = CreateRenderer(caption, TimeLabels());

        renderer.SetViewMode(SpectrogramViewMode.Beats);
        renderer.SetViewBeats(1); // a single beat is Last Beat's job
        Assert.Equal(2, ViewBeats(renderer)); // clamped to 2
        Assert.Equal("Time (ms)", caption.Text);

        renderer.SetViewBeats(8);
        Assert.Equal(8, ViewBeats(renderer)); // honored above the floor
    }

    [Theory]
    [InlineData(250.0, 1000.0, 50.0)] // wide lane -> fine step (~quarter period)
    [InlineData(250.0, 48.0, 250.0)]  // narrow lane -> coarsened to one label per beat
    [InlineData(100.0, 240.0, 25.0)]
    public void ChooseBeatRulerStep_PicksANiceStepThatFitsTheLane(
        double periodMs, double laneWidthPx, double expectedStep)
    {
        Assert.Equal(expectedStep, SpectrogramRenderer.ChooseBeatRulerStep(periodMs, laneWidthPx));
    }

    [Fact]
    public void TryBeatLaneSourceColumn_CentersTheLaneOnTheOnset()
    {
        // beatCols 10 -> laneCenter at +5; the centre pixel maps to the onset column.
        Assert.True(SpectrogramRenderer.TryBeatLaneSourceColumn(
            onsetColumn: 50, laneStart: 0, laneCenter: 5, xi: 5,
            minValid: 0, total: 1000, sourceWidth: 100, out int center));
        Assert.Equal(50, center);

        // The left edge is half a beat before the onset.
        Assert.True(SpectrogramRenderer.TryBeatLaneSourceColumn(
            50, 0, 5, 0, 0, 1000, 100, out int left));
        Assert.Equal(45, left);

        // Source column wraps through the ring-buffer width.
        Assert.True(SpectrogramRenderer.TryBeatLaneSourceColumn(
            150, 0, 5, 5, 0, 1000, 100, out int wrapped));
        Assert.Equal(50, wrapped);
    }

    [Fact]
    public void TryBeatLaneSourceColumn_LeavesColumnsOutsideRetainedHistoryEmpty()
    {
        // Before the oldest retained column.
        Assert.False(SpectrogramRenderer.TryBeatLaneSourceColumn(
            onsetColumn: 2, laneStart: 0, laneCenter: 5, xi: 0,
            minValid: 0, total: 1000, sourceWidth: 100, out _));

        // At/after the newest column.
        Assert.False(SpectrogramRenderer.TryBeatLaneSourceColumn(
            onsetColumn: 998, laneStart: 0, laneCenter: 5, xi: 9,
            minValid: 0, total: 1000, sourceWidth: 100, out _));
    }

    private static int ViewBeats(SpectrogramRenderer renderer) =>
        (int)typeof(SpectrogramRenderer)
            .GetField("_viewBeats", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderer)!;
}
