using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TimeGrapher.App.Views;

internal static class MarkdownDisplayRenderer
{
    private const int MaxMarkdownChars = 32_000;
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
                if (!TryAddBlock(panel, new TextBlock
                {
                    Text = StripInlineMarkdown(heading),
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.Bold,
                    FontSize = HeadingFontSize(level),
                    Margin = new Thickness(0, level <= 2 ? 8 : 4, 0, 0),
                }))
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

            if (!TryAddBlock(panel, new TextBlock
            {
                Text = StripInlineMarkdown(string.Join(' ', paragraph)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                FontWeight = FontWeight.Normal,
            }))
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
            Text = StripInlineMarkdown(text),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            FontWeight = FontWeight.Normal,
        };
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
                var cell = new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 4),
                    Child = new TextBlock
                    {
                        Text = StripInlineMarkdown(cellText),
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = row == 0 ? FontWeight.Bold : FontWeight.Normal,
                    },
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

    private static string StripInlineMarkdown(string text) => text
        .Replace("**", string.Empty, StringComparison.Ordinal)
        .Replace("__", string.Empty, StringComparison.Ordinal)
        .Replace("`", string.Empty, StringComparison.Ordinal);
}
