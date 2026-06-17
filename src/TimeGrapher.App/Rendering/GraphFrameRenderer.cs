using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Small UI-thread facade for shared analysis UI state. Tab-specific rendering lives
/// in RateScopeRenderer and SoundPrintRenderer.
/// </summary>
internal sealed class GraphFrameRenderer
{
    /// <summary>
    /// Placeholder shown before any metrics arrive. Field widths match WatchMetrics.FormatResults
    /// (fixed-width) so the readout never shifts when real values replace the dashes.
    /// </summary>
    public const string PlaceholderResults =
        "ERROR RATE ------ s/d | AMPLITUDE ---° | BEAT ERROR ---- ms | BEAT ----- bph";
    private const string ValueReadoutBrushKey = "ChromeAccentBrush";
    // Labels/units use the muted-but-readable secondary text brush: ChromeBorderBrush
    // (a faint hairline tint) was near-invisible, while the primary text brush read as
    // too heavy next to the accent values.
    private const string FixedReadoutBrushKey = "TextSecondaryBrush";

    private readonly IReadOnlyList<IAnalysisFrameConsumer> _consumers;
    private readonly TextBlock _results;
    private string? _lastResultsText;

    public GraphFrameRenderer(
        IEnumerable<IAnalysisFrameConsumer> consumers,
        TextBlock resultsText)
    {
        _consumers = consumers.ToArray();
        _results = resultsText;
    }

    public void Initialize(AnalysisTabResetContext context)
    {
        foreach (IAnalysisFrameConsumer consumer in _consumers)
        {
            consumer.Initialize(context);
        }
    }

    public void Reset(AnalysisTabResetContext context)
    {
        foreach (IAnalysisFrameConsumer consumer in _consumers)
        {
            consumer.Reset(context);
        }
        SetResults(PlaceholderResults);
    }

    /// <summary>
    /// Renders <paramref name="text"/> into the results readout, coloring the spans wrapped in
    /// WatchMetrics value markers ('{' … '}') with the accent brush and stripping the markers.
    /// Fixed label/unit text uses the secondary text brush; separators and dash placeholders inherit
    /// the default foreground. Rebuilds the inline runs only when the text actually changes.
    /// </summary>
    public void SetResults(string text)
    {
        if (text == _lastResultsText)
        {
            return;
        }

        _lastResultsText = text;
        RenderInto(_results, text);
    }

    private static void RenderInto(TextBlock target, string text)
    {
        InlineCollection inlines = target.Inlines ??= new InlineCollection();
        inlines.Clear();

        int segmentStart = 0;
        bool accent = false;
        for (int i = 0; i <= text.Length; i++)
        {
            bool boundary = i == text.Length
                || text[i] == WatchMetrics.ValueSpanStart
                || text[i] == WatchMetrics.ValueSpanEnd;
            if (!boundary)
            {
                continue;
            }

            if (i > segmentStart)
            {
                if (accent)
                {
                    var run = new Run(text.Substring(segmentStart, i - segmentStart));
                    run.Bind(TextElement.ForegroundProperty, target.GetResourceObservable(ValueReadoutBrushKey));
                    inlines.Add(run);
                }
                else
                {
                    AddFixedReadoutRuns(target, inlines, text.Substring(segmentStart, i - segmentStart));
                }
            }

            if (i < text.Length)
            {
                accent = text[i] == WatchMetrics.ValueSpanStart;
            }

            segmentStart = i + 1;
        }
    }

    private static void AddFixedReadoutRuns(TextBlock target, InlineCollection inlines, string segment)
    {
        int segmentStart = 0;
        bool currentUsesFixedBrush = UsesFixedReadoutBrush(segment[0]);
        for (int i = 1; i <= segment.Length; i++)
        {
            bool boundary = i == segment.Length
                || UsesFixedReadoutBrush(segment[i]) != currentUsesFixedBrush;
            if (!boundary)
            {
                continue;
            }

            var run = new Run(segment.Substring(segmentStart, i - segmentStart));
            if (currentUsesFixedBrush)
            {
                run.Bind(TextElement.ForegroundProperty, target.GetResourceObservable(FixedReadoutBrushKey));
            }

            inlines.Add(run);
            if (i < segment.Length)
            {
                segmentStart = i;
                currentUsesFixedBrush = UsesFixedReadoutBrush(segment[i]);
            }
        }
    }

    private static bool UsesFixedReadoutBrush(char c)
    {
        return c != '|' && c != '-';
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        foreach (IThemedFrameConsumer consumer in _consumers.OfType<IThemedFrameConsumer>())
        {
            consumer.ApplyTheme(theme);
        }
    }

    public void UpdateResults(AnalysisFrame frame)
    {
        if (frame.MetricsUpdate.ResultsUpdated)
        {
            SetResults(frame.MetricsUpdate.ResultsText);
        }
    }
}
