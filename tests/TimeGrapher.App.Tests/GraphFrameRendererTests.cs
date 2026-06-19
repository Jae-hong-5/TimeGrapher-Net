using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Metrics;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class GraphFrameRendererTests
{
    [Fact]
    public void PlaceholderResultsMatchesFixedWidthLayout()
    {
        // Must stay byte-for-byte aligned with WatchMetrics.BuildResults(all-invalid) so the
        // readout does not shift when the first real metrics arrive.
        Assert.Equal(
            "ERROR RATE ------ s/d | Amplitude ---° | BEAT ERROR ---- ms | BEAT ----- bph",
            GraphFrameRenderer.PlaceholderResults);
    }

    [Fact]
    public void ResultsReadoutColorsFixedTextValuesAndSeparatorsSeparately()
    {
        var target = new TextBlock();
        Color fixedReadoutColor = Color.Parse("#6E6E6E");
        target.Resources.Add("TextSecondaryBrush", new SolidColorBrush(fixedReadoutColor));
        target.Resources.Add("ChromeAccentBrush", new SolidColorBrush(Colors.Red));
        var renderer = new GraphFrameRenderer(Array.Empty<IAnalysisFrameConsumer>(), target);
        char start = WatchMetrics.ValueSpanStart;
        char end = WatchMetrics.ValueSpanEnd;
        string text = $"ERROR RATE {start}  +1.2{end} s/d | Amplitude {start}271{end}° | " +
            $"BEAT ERROR {start} 0.3{end} ms | BEAT {start}21600{end} bph";

        renderer.SetResults(text);

        Run[] runs = target.Inlines!.OfType<Run>().ToArray();
        Assert.Equal(
            new[]
            {
                "ERROR RATE ", "  +1.2", " s/d ", "|", " Amplitude ", "271", "° ", "|",
                " BEAT ERROR ", " 0.3", " ms ", "|", " BEAT ", "21600", " bph",
            },
            runs.Select(run => run.Text).ToArray());
        AssertBoundBrush(runs[0], fixedReadoutColor);
        AssertBoundBrush(runs[1], Colors.Red);
        AssertBoundBrush(runs[2], fixedReadoutColor);
        AssertDefaultForeground(runs[3]);
        AssertBoundBrush(runs[4], fixedReadoutColor);
        AssertBoundBrush(runs[5], Colors.Red);
        AssertBoundBrush(runs[6], fixedReadoutColor);
        AssertDefaultForeground(runs[7]);
        AssertBoundBrush(runs[8], fixedReadoutColor);
        AssertBoundBrush(runs[9], Colors.Red);
        AssertBoundBrush(runs[10], fixedReadoutColor);
        AssertDefaultForeground(runs[11]);
        AssertBoundBrush(runs[12], fixedReadoutColor);
        AssertBoundBrush(runs[13], Colors.Red);
        AssertBoundBrush(runs[14], fixedReadoutColor);
    }

    [Fact]
    public void PlaceholderVariableDashesAndSeparatorsKeepDefaultForeground()
    {
        var target = new TextBlock();
        var renderer = new GraphFrameRenderer(Array.Empty<IAnalysisFrameConsumer>(), target);

        renderer.SetResults(GraphFrameRenderer.PlaceholderResults);

        Run[] runs = target.Inlines!.OfType<Run>().ToArray();
        Assert.Contains(runs, run => run.Text == "------" && !run.IsSet(TextElement.ForegroundProperty));
        Assert.Contains(runs, run => run.Text == "---" && !run.IsSet(TextElement.ForegroundProperty));
        Assert.Contains(runs, run => run.Text == "----" && !run.IsSet(TextElement.ForegroundProperty));
        Assert.Contains(runs, run => run.Text == "-----" && !run.IsSet(TextElement.ForegroundProperty));
        Assert.All(runs.Where(run => run.Text == "|"), AssertDefaultForeground);
        Assert.All(
            runs.Where(run => run.Text is not null && run.Text.Any(char.IsLetter)),
            run => Assert.True(run.IsSet(TextElement.ForegroundProperty), run.Text));
    }

    private static void AssertBoundBrush(Run run, Color expectedColor)
    {
        Assert.True(run.IsSet(TextElement.ForegroundProperty), run.Text);
        ISolidColorBrush brush = Assert.IsAssignableFrom<ISolidColorBrush>(
            run.GetValue(TextElement.ForegroundProperty));
        Assert.Equal(expectedColor, brush.Color);
    }

    private static void AssertDefaultForeground(Run run)
    {
        Assert.False(run.IsSet(TextElement.ForegroundProperty), run.Text);
    }

}
