namespace TimeGrapher.App.Tabs;

/// <summary>
/// Per-render context handed to the active tab.
/// ReviewCursorTimeS is the pause-and-review scrub contract: null renders live,
/// a value renders the captured readings at that stream time (seconds). Defined
/// up front so every tab is written against it instead of being retrofitted.
/// </summary>
internal sealed record AnalysisTabRenderContext(
    int SampleRate,
    double? ReviewCursorTimeS = null);
