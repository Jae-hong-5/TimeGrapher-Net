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
    public void SetViewBeatsBelowTwoStaysInBeatsMode()
    {
        var caption = new TextBlock();
        var renderer = CreateRenderer(caption, TimeLabels());

        renderer.SetViewMode(SpectrogramViewMode.Beats);
        renderer.SetViewBeats(1); // clamped to 2 (a single beat is Last Beat's job)
        Assert.Equal("Time (ms)", caption.Text);
    }
}
