using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    public void Render_AppliesBoldAndItalicInlineRuns()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("**굵게** 그리고 *기울임* 표시.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        Assert.Contains(runs, r => r.Text == "굵게" && r.FontWeight == FontWeight.Bold);
        Assert.Contains(runs, r => r.Text == "기울임" && r.FontStyle == FontStyle.Italic);
    }

    [Fact]
    public void Render_RendersTripleEmphasisAsBoldAndItalic()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("***중요*** 경고입니다.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        // The tripled delimiter is combined bold+italic, with no stray '*' left behind.
        Assert.Contains(runs, r => r.Text == "중요" && r.FontWeight == FontWeight.Bold && r.FontStyle == FontStyle.Italic);
        Assert.DoesNotContain(runs, r => r.Text?.Contains('*') == true);
    }

    [Fact]
    public void Render_TreatsBackslashEscapedDelimitersAsLiteral()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render(@"\*강조 아님\* 그리고 \`코드 아님\`.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        // Escaped delimiters render as their literal characters with no emphasis/code applied,
        // and no backslashes are left in the output — so the whole line stays plain text.
        Assert.Equal("*강조 아님* 그리고 `코드 아님`.", paragraph.Text);
    }

    [Fact]
    public void Render_ClosesEmphasisOnUnescapedDelimiterNotEscapedOne()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render(@"*a\*b* 뒤.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        // The escaped \* is literal inside the span; the trailing unescaped * closes it.
        Assert.Contains(runs, r => r.Text == "a*b" && r.FontStyle == FontStyle.Italic);
    }

    [Fact]
    public void Render_ClosesEmphasisOnDelimiterAfterEscapedBackslash()
    {
        HeadlessPlatform.EnsureStarted();

        // An even backslash run before '*' is escaped backslashes, not an escaped star,
        // so the star still closes: "*a\\*" -> italic "a\" (one literal backslash).
        Control rendered = MarkdownDisplayRenderer.Render(@"*a\\* 뒤.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        Assert.Contains(runs, r => r.Text == "a\\" && r.FontStyle == FontStyle.Italic);
    }

    [Fact]
    public void Render_KeepsSnakeCaseUnderscoresLiteral()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("beat_error_ms 값을 확인.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        // No emphasis parsed: intra-word underscores stay verbatim on the single Text path.
        Assert.Equal("beat_error_ms 값을 확인.", paragraph.Text);
    }

    [Fact]
    public void Render_KeepsCodeSpanContentLiteral()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("설정 `a*b*c` 확인.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        // The * inside the code span must not be consumed as emphasis, and the span must be a
        // distinct run - not merged into the surrounding prose - so it can be styled as code.
        Run codeRun = Assert.Single(runs, r => r.Text == "a*b*c");
        Assert.NotNull(codeRun.Foreground);
    }

    [Fact]
    public void Render_StylesCodeSpansWithAccentDistinctFromProse()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("본문 `code` 확인.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        Run codeRun = Assert.Single(runs, r => r.Text == "code");
        Run proseRun = runs.First(r => r.Text != "code");
        // The code span carries the shared App.axaml accent foreground and a chip background
        // (both reused from App.axaml, not ad-hoc colors); the surrounding prose carries
        // neither, so the two are visibly distinct.
        Application.Current!.TryGetResource("ChromeAccentBrush", Application.Current.ActualThemeVariant, out object? accent);
        Application.Current!.TryGetResource("ChromeBorderBrush", Application.Current.ActualThemeVariant, out object? chip);
        Assert.NotNull(accent);
        Assert.NotNull(chip);
        Assert.Same(accent, codeRun.Foreground);
        Assert.Same(chip, codeRun.Background);
        Assert.NotSame(accent, proseRun.Foreground);
        Assert.NotSame(chip, proseRun.Background);
    }

    [Fact]
    public void Render_StylesCodeSpanInsideHeadingWithDistinctChipBackground()
    {
        HeadlessPlatform.EnsureStarted();

        // A heading's text is already bound to the accent foreground, so foreground alone would
        // not set a code span apart inside a heading. The chip background must still distinguish it.
        Control rendered = MarkdownDisplayRenderer.Render("### `rate` 확인");

        var panel = Assert.IsType<StackPanel>(rendered);
        var heading = Assert.IsType<TextBlock>(panel.Children[0]);
        var runs = heading.Inlines!.OfType<Run>().ToList();
        Run codeRun = Assert.Single(runs, r => r.Text == "rate");
        Run proseRun = runs.First(r => r.Text != "rate");
        Application.Current!.TryGetResource("ChromeBorderBrush", Application.Current.ActualThemeVariant, out object? chip);
        Assert.NotNull(chip);
        Assert.Same(chip, codeRun.Background);
        Assert.NotSame(chip, proseRun.Background);
    }

    [Fact]
    public void Render_KeepsBackslashesInCodeSpanLiteral()
    {
        HeadlessPlatform.EnsureStarted();

        // Code-span content is verbatim per CommonMark: a backslash inside `code` is literal
        // (not an escape), so a Windows-style path survives and the trailing \ before ` does
        // NOT suppress the closing backtick. This is why escaped backticks are not skipped
        // when closing a code span.
        Control rendered = MarkdownDisplayRenderer.Render(@"경로 `C:\dir\` 참고.");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        string text = string.Concat(paragraph.Inlines!.OfType<Run>().Select(r => r.Text));
        Assert.Contains(@"C:\dir\", text);
        Assert.DoesNotContain("`", text);  // the backticks were consumed - the span closed
    }

    [Fact]
    public void Render_LeavesUnmatchedEmphasisAsLiteral()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render("3 * 4 = 12");

        var panel = Assert.IsType<StackPanel>(rendered);
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.Equal("3 * 4 = 12", paragraph.Text);
    }

    [Fact]
    public void Render_TruncatesLongInputForDisplay()
    {
        HeadlessPlatform.EnsureStarted();

        Control rendered = MarkdownDisplayRenderer.Render(new string('x', 65_000));

        var panel = Assert.IsType<StackPanel>(rendered);
        Assert.Equal(2, panel.Children.Count);
        // The rendered content must actually be bounded, not merely accompanied by a
        // truncation notice: the paragraph block must not exceed the input char cap.
        var paragraph = Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.True(
            paragraph.Text!.Length <= 64_000,
            $"rendered paragraph length {paragraph.Text!.Length} exceeded the 64000-char cap");
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
