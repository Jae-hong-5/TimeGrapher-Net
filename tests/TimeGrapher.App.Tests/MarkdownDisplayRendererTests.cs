using Avalonia.Controls;
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
        Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.IsType<Grid>(panel.Children[1]);
        Assert.IsType<Grid>(panel.Children[2]);
    }
}
