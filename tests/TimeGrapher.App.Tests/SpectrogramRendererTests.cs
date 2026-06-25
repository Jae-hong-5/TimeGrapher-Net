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
        caption,
        new Border());

    private static TextBlock[] TimeLabels() =>
        new[] { new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock() };

    [Fact]
    public void BeatsModeCaptionReportsTheBeatCount()
    {
        var caption = new TextBlock();
        var renderer = CreateRenderer(caption, TimeLabels());

        renderer.SetViewMode(SpectrogramViewMode.Beats);
        Assert.Equal("Time (ms) · last 2 beats", caption.Text);

        renderer.SetViewBeats(8);
        Assert.Equal("Time (ms) · last 8 beats", caption.Text);
    }

    [Fact]
    public void BeatsModeWindowSpansBeatCountTimesTheBeatPeriod()
    {
        var caption = new TextBlock();
        TextBlock[] timeLabels = TimeLabels();
        var renderer = CreateRenderer(caption, timeLabels);

        // Before the first beat is detected the renderer falls back to a 0.125 s
        // beat period (28800 BPH), so two beats span 250 ms — the last time label
        // is the full-window value in ms.
        renderer.SetViewMode(SpectrogramViewMode.Beats);
        Assert.Equal("250", timeLabels[^1].Text);

        // Four beats span 500 ms on the same fallback period.
        renderer.SetViewBeats(4);
        Assert.Equal("500", timeLabels[^1].Text);
    }

    [Fact]
    public void SetViewBeatsClampsBelowTwo()
    {
        var caption = new TextBlock();
        var renderer = CreateRenderer(caption, TimeLabels());

        renderer.SetViewMode(SpectrogramViewMode.Beats);
        renderer.SetViewBeats(1); // a single beat is Last Beat's job, not a compare view
        Assert.Equal("Time (ms) · last 2 beats", caption.Text);
    }
}
