using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace TimeGrapher.App.Views;

internal static class MarkdownDisplayRenderer
{
    private const int MaxMarkdownChars = 64_000;
    private const int MaxBlocks = 200;
    private const int MaxTableRows = 40;
    private const int MaxTableColumns = 6;
    private const string TruncationNotice = "AI analysis truncated for display.";

    public static Control Render(string markdown)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
        };

        bool inputTruncated = markdown.Length > MaxMarkdownChars;
        string source = inputTruncated ? markdown[..MaxMarkdownChars] : markdown;
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (IsRule(trimmed))
            {
                if (!TryAddBlock(panel, new Border
                {
                    Height = 1,
                    Background = Brushes.Gray,
                    Opacity = 0.35,
                    Margin = new Thickness(0, 2),
                }))
                {
                    AddTruncationNotice(panel);
                    return panel;
                }

                continue;
            }

            if (IsTableLine(trimmed))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && IsTableLine(lines[i].Trim()))
                {
                    tableLines.Add(lines[i].Trim());
                    i++;
                }

                i--;
                if (!TryAddBlock(panel, BuildTable(tableLines)))
                {
                    AddTruncationNotice(panel);
                    return panel;
                }

                continue;
            }

            if (TryParseHeading(trimmed, out int level, out string heading))
            {
                var headingBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.Bold,
                    FontSize = HeadingFontSize(level),
                    Margin = new Thickness(0, level <= 2 ? 8 : 4, 0, 0),
                };
                // Headings use the app accent brush (theme-aware); inline runs inherit it.
                headingBlock.Bind(TextBlock.ForegroundProperty, headingBlock.GetResourceObservable("ChromeAccentBrush"));
                ApplyInline(headingBlock, heading);
                if (!TryAddBlock(panel, headingBlock))
                {
                    AddTruncationNotice(panel);
                    return panel;
                }

                continue;
            }

            if (TryParseListItem(trimmed, out string marker, out string itemText))
            {
                if (!TryAddBlock(panel, BuildListItem(marker, itemText)))
                {
                    AddTruncationNotice(panel);
                    return panel;
                }

                continue;
            }

            var paragraph = new List<string> { trimmed };
            while (i + 1 < lines.Length && IsParagraphContinuation(lines[i + 1].Trim()))
            {
                i++;
                paragraph.Add(lines[i].Trim());
            }

            var paragraphBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                FontWeight = FontWeight.Normal,
            };
            ApplyInline(paragraphBlock, string.Join(' ', paragraph));
            if (!TryAddBlock(panel, paragraphBlock))
            {
                AddTruncationNotice(panel);
                return panel;
            }
        }

        if (inputTruncated)
        {
            AddTruncationNotice(panel);
        }

        return panel;
    }

    private static bool IsParagraphContinuation(string line) =>
        line.Length > 0 &&
        !IsRule(line) &&
        !IsTableLine(line) &&
        !TryParseHeading(line, out _, out _) &&
        !TryParseListItem(line, out _, out _);

    private static bool IsRule(string line) => line is "---" or "***" or "___";

    private static bool IsTableLine(string line) => line.StartsWith('|') && line.EndsWith('|') && line.Count(static c => c == '|') >= 2;

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        level = 0;
        while (level < line.Length && level < 6 && line[level] == '#')
        {
            level++;
        }

        if (level == 0 || level >= line.Length || line[level] != ' ')
        {
            text = string.Empty;
            return false;
        }

        text = line[(level + 1)..].Trim();
        return text.Length > 0;
    }

    private static double HeadingFontSize(int level) => level switch
    {
        <= 1 => 28.0,
        2 => 24.0,
        3 => 20.0,
        _ => 17.0
    };

    private static bool TryParseListItem(string line, out string marker, out string text)
    {
        marker = string.Empty;
        text = string.Empty;
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            marker = "•";
            text = line[2..].Trim();
            return text.Length > 0;
        }

        int dot = line.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot + 1 < line.Length && line[dot + 1] == ' ')
        {
            string number = line[..dot];
            if (number.All(char.IsDigit))
            {
                marker = number + ".";
                text = line[(dot + 2)..].Trim();
                return text.Length > 0;
            }
        }

        return false;
    }

    private static Control BuildListItem(string marker, string text)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
        };
        grid.Children.Add(new TextBlock
        {
            Text = marker,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Top,
        });
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            FontWeight = FontWeight.Normal,
        };
        ApplyInline(body, text);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static Control BuildTable(IReadOnlyList<string> tableLines)
    {
        string[][] allRows = tableLines
            .Where(static line => !IsTableSeparator(line))
            .Select(SplitTableLine)
            .Where(static cells => cells.Length > 0)
            .ToArray();
        if (allRows.Length == 0)
        {
            return new TextBlock { Text = string.Join(Environment.NewLine, tableLines), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Normal };
        }

        bool truncatedRows = allRows.Length > MaxTableRows;
        bool truncatedColumns = allRows.Any(static row => row.Length > MaxTableColumns);
        string[][] rows = allRows
            .Take(MaxTableRows)
            .Select(static row => row.Take(MaxTableColumns).ToArray())
            .ToArray();
        int columnCount = rows.Max(static row => row.Length);
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", rows.Length))),
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columnCount))),
        };

        for (int row = 0; row < rows.Length; row++)
        {
            for (int col = 0; col < columnCount; col++)
            {
                string cellText = col < rows[row].Length ? rows[row][col] : string.Empty;
                var cellBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = row == 0 ? FontWeight.Bold : FontWeight.Normal,
                };
                ApplyInline(cellBlock, cellText);
                var cell = new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 4),
                    Child = cellBlock,
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);
                grid.Children.Add(cell);
            }
        }

        if (truncatedRows || truncatedColumns)
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(grid);
            panel.Children.Add(BuildTruncationText());
            return panel;
        }

        return grid;
    }

    private static string[] SplitTableLine(string line) => line.Trim('|')
        .Split('|')
        .Select(static cell => cell.Trim())
        .ToArray();

    private static bool IsTableSeparator(string line)
    {
        string content = line.Trim('|', ' ');
        if (content.Length == 0)
        {
            return false;
        }

        return content.All(static c => c is '-' or ':' or '|' or ' ');
    }

    private static bool TryAddBlock(StackPanel panel, Control control)
    {
        if (panel.Children.Count >= MaxBlocks)
        {
            return false;
        }

        panel.Children.Add(control);
        return true;
    }

    private static void AddTruncationNotice(StackPanel panel) => panel.Children.Add(BuildTruncationText());

    private static TextBlock BuildTruncationText() => new()
    {
        Text = TruncationNotice,
        TextWrapping = TextWrapping.Wrap,
        FontStyle = FontStyle.Italic,
        FontWeight = FontWeight.Normal,
        Opacity = 0.8,
    };

    // Renders inline markdown (**bold**, __bold__, *italic*, _italic_, ***bold italic***,
    // `code`, and \-escaped literal delimiters) into the text block. Plain text with no inline
    // markers keeps the single Text path so the common case is unchanged and the display char
    // cap stays enforced on Text directly.
    private static void ApplyInline(TextBlock block, string text)
    {
        var runs = new List<InlineRun>();
        ParseInline(text, runs, bold: false, italic: false);

        if (runs.Count <= 1 && (runs.Count == 0 || runs[0] is { Bold: false, Italic: false, Code: false }))
        {
            block.Text = runs.Count == 0 ? text : runs[0].Text;
            return;
        }

        bool haveAccent = TryGetBrush("ChromeAccentBrush", out IBrush? codeForeground);
        bool haveChip = TryGetBrush("ChromeBorderBrush", out IBrush? codeBackground);
        InlineCollection inlines = block.Inlines ??= new InlineCollection();
        foreach (InlineRun run in runs)
        {
            var element = new Run(run.Text);
            if (run.Bold)
            {
                element.FontWeight = FontWeight.Bold;
            }

            if (run.Italic)
            {
                element.FontStyle = FontStyle.Italic;
            }

            if (run.Code)
            {
                // The AI window font is already monospace, so a code span is set apart by color:
                // a subtle chip background plus the app accent foreground, both reused from
                // App.axaml (theme-aware). The background is what keeps it distinct even inside a
                // heading, whose text already carries the accent foreground. Resolved eagerly so
                // the run is styled even before the block attaches to a visual tree.
                if (haveChip)
                {
                    element.Background = codeBackground;
                }

                if (haveAccent)
                {
                    element.Foreground = codeForeground;
                }
            }

            inlines.Add(element);
        }
    }

    private static bool TryGetBrush(string key, out IBrush? brush)
    {
        brush = null;
        if (Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out object? resource)
            && resource is IBrush found)
        {
            brush = found;
            return true;
        }

        return false;
    }

    private static void ParseInline(string text, List<InlineRun> runs, bool bold, bool italic)
    {
        var literal = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Backslash escapes the next delimiter so it renders literally and is not read
            // as emphasis or as a code-span opener (CommonMark backslash escape, limited to
            // the delimiters this renderer understands). "\*not emphasis\*" keeps its literal
            // asterisks. Inside an OPEN code span, content stays verbatim per CommonMark - a
            // backslash there is literal (so a "C:\dir\" code span survives intact) and a
            // backtick still closes the span; escapes apply only in normal text.
            if (c == '\\' && i + 1 < text.Length && IsEscapable(text[i + 1]))
            {
                literal.Append(text[i + 1]);
                i += 2;
                continue;
            }

            // `code`: content is literal (the app font is already monospace); parsing it here
            // keeps any * or _ inside a code span from being read as emphasis.
            if (c == '`')
            {
                int codeClose = text.IndexOf('`', i + 1);
                if (codeClose > i)
                {
                    FlushLiteral(runs, literal, bold, italic);
                    string code = text[(i + 1)..codeClose];
                    if (code.Length > 0)
                    {
                        runs.Add(new InlineRun(code, bold, italic, Code: true));
                    }

                    i = codeClose + 1;
                    continue;
                }
            }

            // ***bold italic*** / ___bold italic___: a triple delimiter run opens combined
            // strong+emphasis. Checked before the bold branch so the third delimiter is not
            // left as a stray literal (the shape AI output favours for strong warnings).
            if ((c == '*' || c == '_') && !bold && !italic
                && i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c
                && IsLeftFlank(text, i, 3, c))
            {
                int close = FindClosing(text, i + 3, c, 3);
                if (close >= 0)
                {
                    FlushLiteral(runs, literal, bold, italic);
                    ParseInline(text[(i + 3)..close], runs, bold: true, italic: true);
                    i = close + 3;
                    continue;
                }
            }

            // **bold** / __bold__
            if ((c == '*' || c == '_') && !bold && i + 1 < text.Length && text[i + 1] == c && IsLeftFlank(text, i, 2, c))
            {
                int close = FindClosing(text, i + 2, c, 2);
                if (close >= 0)
                {
                    FlushLiteral(runs, literal, bold, italic);
                    ParseInline(text[(i + 2)..close], runs, bold: true, italic: italic);
                    i = close + 2;
                    continue;
                }
            }

            // *italic* / _italic_ (a doubled delimiter is handled by the bold branch above)
            if ((c == '*' || c == '_') && !italic && !(i + 1 < text.Length && text[i + 1] == c) && IsLeftFlank(text, i, 1, c))
            {
                int close = FindClosing(text, i + 1, c, 1);
                if (close >= 0)
                {
                    FlushLiteral(runs, literal, bold, italic);
                    ParseInline(text[(i + 1)..close], runs, bold: bold, italic: true);
                    i = close + 1;
                    continue;
                }
            }

            literal.Append(c);
            i++;
        }

        FlushLiteral(runs, literal, bold, italic);
    }

    private static void FlushLiteral(List<InlineRun> runs, StringBuilder literal, bool bold, bool italic)
    {
        if (literal.Length > 0)
        {
            runs.Add(new InlineRun(literal.ToString(), bold, italic));
            literal.Clear();
        }
    }

    // Finds the matching closing delimiter run, returning its start index or -1 when the
    // emphasis is never closed (so the opener is left as literal text).
    private static int FindClosing(string text, int start, char delim, int len)
    {
        for (int j = start; j + len <= text.Length; j++)
        {
            if (!IsDelimiterRun(text, j, delim, len))
            {
                continue;
            }

            // Reject empty content, a closer preceded by whitespace ("a *b* c" closes,
            // "a * b *" does not), and a backslash-escaped delimiter ("a\*b*" closes on
            // the final unescaped star, not the escaped one).
            if (j == start || char.IsWhiteSpace(text[j - 1]) || IsBackslashEscaped(text, j))
            {
                continue;
            }

            // Intra-word underscore (a_b_c) is not emphasis: the closer must also sit on a
            // word boundary.
            if (delim == '_' && j + len < text.Length && char.IsLetterOrDigit(text[j + len]))
            {
                continue;
            }

            return j;
        }

        return -1;
    }

    // Delimiters a leading backslash renders literally (the subset this renderer parses).
    private static bool IsEscapable(char c) => c is '*' or '_' or '`' or '\\';

    // A delimiter at <paramref name="index"/> is escaped only when preceded by an ODD
    // number of backslashes; an even run is escaped backslash pairs ("a\\*") that leave
    // the delimiter itself active, so the emphasis still closes on it.
    private static bool IsBackslashEscaped(string text, int index)
    {
        int backslashes = 0;
        for (int k = index - 1; k >= 0 && text[k] == '\\'; k--)
        {
            backslashes++;
        }

        return (backslashes & 1) == 1;
    }

    private static bool IsDelimiterRun(string text, int index, char delim, int len)
    {
        for (int k = 0; k < len; k++)
        {
            if (text[index + k] != delim)
            {
                return false;
            }
        }

        return true;
    }

    // Left-flanking test: the opener must be followed by non-whitespace, and an underscore
    // opener must not sit inside a word (so snake_case identifiers are left untouched).
    private static bool IsLeftFlank(string text, int index, int len, char delim)
    {
        int after = index + len;
        if (after >= text.Length || char.IsWhiteSpace(text[after]))
        {
            return false;
        }

        if (delim == '_' && index > 0 && char.IsLetterOrDigit(text[index - 1]))
        {
            return false;
        }

        return true;
    }

    private readonly record struct InlineRun(string Text, bool Bold, bool Italic, bool Code = false);
}
