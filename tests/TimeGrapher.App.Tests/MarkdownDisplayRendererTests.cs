using Avalonia.Controls;
using Avalonia.Media;
using TimeGrapher.App.Views;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MarkdownDisplayRendererTests
{
    [Fact]
    public void Render_TurnsHeadingsBulletsAndTablesIntoControls()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("""
### 결론
- **일오차**가 빠릅니다.

| 지표 | 값 |
|---|---|
| Rate | +30.5 s/d |
""");

        var panel = Assert.IsType<StackPanel>(rendered);
        var heading = Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.Equal(20.0, heading.FontSize);
        var list = Assert.IsType<Grid>(panel.Children[1]);
        Assert.Equal(FontWeight.Normal, Assert.IsType<TextBlock>(list.Children[1]).FontWeight);
        Assert.IsType<Grid>(panel.Children[2]);
    }

    [Fact]
    public void Render_UsesLargeHeadingSizesForTitles()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("""
# 큰 제목
## 중간 제목
#### 작은 제목
""");

        var panel = Assert.IsType<StackPanel>(rendered);
        Assert.Equal(28.0, Assert.IsType<TextBlock>(panel.Children[0]).FontSize);
        Assert.Equal(24.0, Assert.IsType<TextBlock>(panel.Children[1]).FontSize);
        Assert.Equal(17.0, Assert.IsType<TextBlock>(panel.Children[2]).FontSize);
    }

    [Fact]
    public void Render_UsesNormalWeightForParagraphBody()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("본문 설명입니다.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.Equal(FontWeight.Normal, paragraph.FontWeight);
    }

    [Fact]
    public void Render_TruncatesLongInputForDisplay()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render(new string('x', 33_000));

        var panel = Assert.IsType<StackPanel>(rendered);
        Assert.Equal(2, panel.Children.Count);
        var notice = Assert.IsType<TextBlock>(panel.Children[1]);
        Assert.Contains("truncated", notice.Text);
    }

    [Fact]
    public void Render_LimitsBlockCount()
    {
        HeadlessPlatform.EnsureStarted();
        string markdown = string.Join('\n', Enumerable.Range(0, 240).Select(static i => "- item " + i));

        Control rendered = MarkdownDisplayRenderer.Render(markdown);

        var panel = Assert.IsType<StackPanel>(rendered);
        Assert.True(panel.Children.Count <= 201);
        var notice = Assert.IsType<TextBlock>(panel.Children[^1]);
        Assert.Contains("truncated", notice.Text);
    }

    [Fact]
    public void Render_LimitsLargeTables()
    {
        HeadlessPlatform.EnsureStarted();
        string header = "| c1 | c2 | c3 | c4 | c5 | c6 | c7 |";
        string separator = "|---|---|---|---|---|---|---|";
        string rows = string.Join('\n', Enumerable.Range(0, 45).Select(static i => $"| {i} | a | b | c | d | e | f |"));

        Control rendered = MarkdownDisplayRenderer.Render(string.Join('\n', header, separator, rows));

        var panel = Assert.IsType<StackPanel>(rendered);
        var tableWrapper = Assert.IsType<StackPanel>(panel.Children[0]);
        Assert.IsType<Grid>(tableWrapper.Children[0]);
        var notice = Assert.IsType<TextBlock>(tableWrapper.Children[1]);
        Assert.Contains("truncated", notice.Text);
    }
}
