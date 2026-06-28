using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Tabs;

internal enum InfoTabKind
{
    RateScope,
    SoundPrint,
    TraceDisplay,
    ScopeSweep,
    Vario,
    BeatErrorDiag,
    MultiFilterScope,
    LongTermPerformance,
    WatchPositions,
    WatchHealthRadar,
    BeatNoiseScope,
    EscapementAnalyzer,
    WaveformCompare,
    Spectrogram,
}

internal enum GraphSeriesRenderMode
{
    Line,
    Points,
}

internal sealed record GraphSeriesDefinition(
    string Id,
    string Name,
    uint Color,
    GraphSeriesRenderMode RenderMode,
    int TargetPointBudget,
    // Per-series fill opacity (0-255); byte so an out-of-range value is a
    // compile error instead of a silent wrap when applied via WithAlpha.
    byte FillAlpha = 0);

internal sealed record InfoTabDefinition(
    string Id,
    string Title,
    InfoTabKind Kind,
    int RefreshIntervalMs,
    bool UsesGraphSnapshots,
    IReadOnlyList<GraphSeriesDefinition> GraphSeries);

internal static class InfoTabCatalog
{
    public const string RateScopeTabId = "rate-scope";
    public const string SoundPrintTabId = "sound-print";
    public const string TraceDisplayTabId = "trace-display";
    public const string ScopeSweepTabId = "scope-sweep";
    public const string VarioTabId = "rate-amplitude-stability";
    public const string BeatErrorDiagTabId = "beat-error-diag";
    public const string MultiFilterScopeTabId = "multi-filter-scope";
    public const string LongTermPerfTabId = "long-term-perf";
    public const string WatchPositionsTabId = "watch-positions";
    public const string WatchHealthRadarTabId = "watch-health-radar";
    public const string BeatNoiseScopeTabId = "beat-noise-scope";
    public const string EscapementAnalyzerTabId = "escapement-analyzer";
    public const string WaveformCompareTabId = "waveform-compare";
    public const string SpectrogramTabId = "spectrogram";

    public const int DefaultUiRefreshIntervalMs = 33;
    public const int SoundPrintRefreshIntervalMs = 100;
    // Scope point budget, used both as the Core decimation budget over the 2 s
    // stride-reference window (ScopeRateFrameProjector) and as the renderer's
    // view-limited reduction budget over the visible window. 32000 points across a
    // 2 s window is ~0.0625 ms/point at 48 kHz — the same time resolution the Sweep
    // tab paints (SweepFrameProjector.SweepBinBudget over one tick-tick window).
    public const int ScopeTargetPointBudget = 32000;
    public const int RateTargetPointBudget = 250;

    private static readonly GraphSeriesDefinition[] RateScopeSeries =
    {
        new(AnalysisGraphSeries.ScopePcm, "Rectified", Argb.Blue, GraphSeriesRenderMode.Line, ScopeTargetPointBudget, FillAlpha: 20),
        new(AnalysisGraphSeries.ScopeThreshold, "Trigger", Argb.Red, GraphSeriesRenderMode.Line, ScopeTargetPointBudget),
        new(AnalysisGraphSeries.RateTic, "Tic", Argb.Red, GraphSeriesRenderMode.Points, RateTargetPointBudget),
        new(AnalysisGraphSeries.RateToc, "Toc", Argb.Blue, GraphSeriesRenderMode.Points, RateTargetPointBudget),
    };

    // Same tic/toc Error Rate traces the Rate/Scope tab consumes; declared
    // separately so each tab states its own graph-series contract.
    private static readonly GraphSeriesDefinition[] BeatErrorDiagSeries =
    {
        new(AnalysisGraphSeries.RateTic, "Tic", Argb.Red, GraphSeriesRenderMode.Points, RateTargetPointBudget),
        new(AnalysisGraphSeries.RateToc, "Toc", Argb.Blue, GraphSeriesRenderMode.Points, RateTargetPointBudget),
    };

    private static readonly InfoTabDefinition[] Definitions = BuildDefinitions();

    private static InfoTabDefinition[] BuildDefinitions()
    {
        // Tab order follows the measurement workflow: live signal and primary
        // readings first, then long-term/positional, then the waveform-diagnostic
        // tools. The 13 tabs wrap to two rows (7 + 6), so Positions leads the
        // second row. Rate/Scope stays first (Definitions[0]) and Spectrogram last.
        var definitions = new List<InfoTabDefinition>
        {
            new(RateScopeTabId, "Rate/Scope", InfoTabKind.RateScope, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: true, RateScopeSeries),
            // Beat Error Diag plots the per-frame tic/toc Error Rate traces and reads the
            // cumulative snapshot for its numeric panel and diagnostic rules.
            new(BeatErrorDiagTabId, "Beat Error", InfoTabKind.BeatErrorDiag, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: true, BeatErrorDiagSeries),
            // Trace Display renders the cumulative BeatMetricsHistorySnapshot the
            // frame carries; it declares no per-frame graph-series contract.
            new(TraceDisplayTabId, "Trace", InfoTabKind.TraceDisplay, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Vario stability gauges render the running stats on the same snapshot.
            new(VarioTabId, "Vario", InfoTabKind.Vario, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Long-Term Performance renders the cumulative BeatMetricsHistorySnapshot
            // (bucket averages plus YMin/YMax variation bands); it declares no
            // per-frame graph-series contract.
            new(LongTermPerfTabId, "Long-Term", InfoTabKind.LongTermPerformance, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Scope Sweep refills its single plot from the Core-folded sweep.trace
            // replace series; the fixed bin budget lives Core-side, so no per-frame
            // graph-series reduction contract is declared here.
            new(ScopeSweepTabId, "Sweep", InfoTabKind.ScopeSweep, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Escapement Analyzer renders the latest segment of the same
            // cumulative BeatSegmentsSnapshot (A / C marker lines with ms
            // labels and the onset-vs-peak repeatability panel); it declares
            // no per-frame graph-series contract.
            new(EscapementAnalyzerTabId, "Escapement", InfoTabKind.EscapementAnalyzer, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Positions reads the cumulative snapshot's ActivePosition stamp
            // and per-position aggregates; it declares no per-frame graph-series
            // contract. Placed eighth so it leads the wrapped second tab row.
            new(WatchPositionsTabId, "Positions", InfoTabKind.WatchPositions, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Watch Health renders a six-position radar of the per-position
            // aggregates the same cumulative snapshot already carries (amplitude /
            // rate / beat error by position), with the shared accept band as the
            // healthy ring; it declares no per-frame graph-series contract. Placed
            // right after Positions since it reuses that multi-position data.
            new(WatchHealthRadarTabId, "Health", InfoTabKind.WatchHealthRadar, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Beat Noise renders the cumulative BeatSegmentsSnapshot the
            // frame carries (Scope 1 segments + Scope 2 lane averages); it
            // declares no per-frame graph-series contract.
            new(BeatNoiseScopeTabId, "Beat Noise", InfoTabKind.BeatNoiseScope, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Waveform Compare stacks the recent beats of the same cumulative
            // BeatSegmentsSnapshot in A-aligned, peak-normalized lanes with the
            // A / mean-C timing guides; it declares no per-frame graph-series
            // contract.
            new(WaveformCompareTabId, "Comparison", InfoTabKind.WaveformCompare, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Filter Scope refills its four stacked plots from the
            // Core-decimated filter.f0..f3 replace series; the per-series point
            // budget lives Core-side (MultiFilterFrameProjector), so no per-frame
            // graph-series reduction contract is declared here.
            new(MultiFilterScopeTabId, "Filter Scope", InfoTabKind.MultiFilterScope, DefaultUiRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Sound Print paints the per-beat envelope image on the 100 ms
            // cadence; it declares no per-frame graph-series contract.
            new(SoundPrintTabId, "Sound Print", InfoTabKind.SoundPrint, SoundPrintRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
            // Spectrogram renders the Core-built STFT image the frame carries
            // (the Sound Print pattern); the image publishes on the sound-print
            // 100 ms cadence, so the tab refreshes on the same interval and
            // declares no per-frame graph-series contract.
            new(SpectrogramTabId, "Spectrogram", InfoTabKind.Spectrogram, SoundPrintRefreshIntervalMs, UsesGraphSnapshots: false, Array.Empty<GraphSeriesDefinition>()),
        };

        return definitions.ToArray();
    }

    public static IReadOnlyList<InfoTabDefinition> All => Definitions;

    public static InfoTabDefinition RateScope => Definitions[0];

    public static InfoTabDefinition Get(string id)
    {
        foreach (InfoTabDefinition definition in Definitions)
        {
            if (definition.Id == id)
            {
                return definition;
            }
        }

        throw new ArgumentException($"Unknown info tab '{id}'.", nameof(id));
    }

    public static bool TryGet(string id, out InfoTabDefinition? definition)
    {
        foreach (InfoTabDefinition candidate in Definitions)
        {
            if (candidate.Id == id)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }
}
