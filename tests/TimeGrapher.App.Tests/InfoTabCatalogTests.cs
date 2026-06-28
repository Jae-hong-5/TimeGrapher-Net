using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class InfoTabCatalogTests
{
    [Fact]
    public void TabIdsAreUniqueAndHaveRefreshPolicies()
    {
        string[] tabIds = InfoTabCatalog.All.Select(tab => tab.Id).ToArray();

        Assert.Equal(tabIds.Length, tabIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(InfoTabCatalog.All, tab => Assert.InRange(tab.RefreshIntervalMs, 1, int.MaxValue));
    }

    [Fact]
    public void LiveGraphTabsUseDisplayRateRefreshCadence()
    {
        Assert.Equal(16, InfoTabCatalog.DefaultUiRefreshIntervalMs);
        Assert.All(
            InfoTabCatalog.All.Where(tab =>
                tab.Kind is not InfoTabKind.SoundPrint and not InfoTabKind.Spectrogram),
            tab => Assert.Equal(InfoTabCatalog.DefaultUiRefreshIntervalMs, tab.RefreshIntervalMs));
    }

    [Fact]
    public void RateScopeTabDeclaresSnapshotGraphContract()
    {
        InfoTabDefinition tab = InfoTabCatalog.Get(InfoTabCatalog.RateScopeTabId);
        string[] requiredIds =
        {
            AnalysisGraphSeries.ScopePcm,
            AnalysisGraphSeries.ScopeThreshold,
            AnalysisGraphSeries.RateTic,
            AnalysisGraphSeries.RateToc,
        };

        HashSet<string> seriesIds = tab.GraphSeries.Select(series => series.Id).ToHashSet(StringComparer.Ordinal);

        Assert.True(tab.UsesGraphSnapshots);
        Assert.All(requiredIds, id => Assert.Contains(id, seriesIds));
        Assert.Equal(
            new[] { "Rectified", "Trigger", "Tic", "Toc" },
            tab.GraphSeries.Select(series => series.Name).ToArray());
        Assert.All(tab.GraphSeries, series => Assert.InRange(series.TargetPointBudget, 1, int.MaxValue));
    }

    [Fact]
    public void SoundPrintTabIsIndependentFromGraphSeries()
    {
        InfoTabDefinition tab = InfoTabCatalog.Get(InfoTabCatalog.SoundPrintTabId);

        Assert.False(tab.UsesGraphSnapshots);
        Assert.Empty(tab.GraphSeries);
        Assert.Equal(InfoTabKind.SoundPrint, tab.Kind);
    }

    [Fact]
    public void TryGetReturnsFalseForUnknownTab()
    {
        Assert.False(InfoTabCatalog.TryGet("missing", out InfoTabDefinition? tab));
        Assert.Null(tab);
    }

    [Fact]
    public void SnapshotlessTabsDeclareNoGraphSeries()
    {
        // One invariant replaces eight per-tab tautologies that each re-asserted
        // the (Kind, !UsesGraphSnapshots, Empty GraphSeries) trio straight out of
        // the catalog data table: every tab that does not use graph snapshots must
        // declare no graph series. Tabs WITH a distinctive contract (RateScope,
        // BeatErrorDiag, SoundPrint, Spectrogram, Positions) keep their own tests.
        Assert.All(
            InfoTabCatalog.All.Where(tab => !tab.UsesGraphSnapshots),
            tab => Assert.Empty(tab.GraphSeries));
    }

    [Fact]
    public void BeatErrorDiagTabDeclaresRateTraceContract()
    {
        InfoTabDefinition tab = InfoTabCatalog.Get(InfoTabCatalog.BeatErrorDiagTabId);

        Assert.Equal(InfoTabKind.BeatErrorDiag, tab.Kind);
        Assert.True(tab.UsesGraphSnapshots);
        Assert.Equal(
            new[] { AnalysisGraphSeries.RateTic, AnalysisGraphSeries.RateToc },
            tab.GraphSeries.Select(series => series.Id).ToArray());
        Assert.Equal(
            new[] { "Tic", "Toc" },
            tab.GraphSeries.Select(series => series.Name).ToArray());
        Assert.All(tab.GraphSeries, series =>
            Assert.Equal(GraphSeriesRenderMode.Points, series.RenderMode));
    }

    [Fact]
    public void PositionsTabCombinesSelectionAndSequenceHistoryWithoutGraphSeries()
    {
        InfoTabDefinition tab = InfoTabCatalog.Get(InfoTabCatalog.WatchPositionsTabId);

        Assert.Equal(InfoTabKind.WatchPositions, tab.Kind);
        Assert.Equal("Positions", tab.Title);
        Assert.False(tab.UsesGraphSnapshots);
        Assert.Empty(tab.GraphSeries);
    }

    [Fact]
    public void WaveformCompareTabUsesComparisonTitle()
    {
        InfoTabDefinition tab = InfoTabCatalog.Get(InfoTabCatalog.WaveformCompareTabId);

        Assert.Equal(InfoTabKind.WaveformCompare, tab.Kind);
        Assert.Equal("Comparison", tab.Title);
    }

    [Fact]
    public void SpectrogramTabRendersFromFrameImageNotGraphSeries()
    {
        InfoTabDefinition tab = InfoTabCatalog.Get(InfoTabCatalog.SpectrogramTabId);

        Assert.Equal(InfoTabKind.Spectrogram, tab.Kind);
        Assert.Equal(InfoTabCatalog.SoundPrintRefreshIntervalMs, tab.RefreshIntervalMs);
        Assert.False(tab.UsesGraphSnapshots);
        Assert.Empty(tab.GraphSeries);
    }

    [Fact]
    public void SpectrogramIsTheLastCatalogTab()
    {
        // The Settings tab was removed (its run options moved to the gear-button
        // popup), so Spectrogram is now the final analysis-display tab.
        InfoTabDefinition tab = InfoTabCatalog.All.Last();

        Assert.Equal(InfoTabCatalog.SpectrogramTabId, tab.Id);
        Assert.Equal(InfoTabKind.Spectrogram, tab.Kind);
    }

    [Fact]
    public void EveryKindAppearsExactlyOnce()
    {
        // Derived invariants instead of hardcoded counts (which this wave had
        // to bump in every tab commit while catching nothing): each
        // InfoTabKind backs exactly one catalog tab and the kinds in the
        // catalog exactly cover the enum. A duplicate kind or a kind without
        // a tab fails loudly; adding tab #14 needs no edit here beyond its
        // new kind.
        InfoTabKind[] expectedKinds = Enum.GetValues<InfoTabKind>()
            .OrderBy(kind => kind)
            .ToArray();
        InfoTabKind[] catalogKinds = InfoTabCatalog.All
            .Select(tab => tab.Kind)
            .OrderBy(kind => kind)
            .ToArray();

        Assert.Equal(expectedKinds, catalogKinds);
    }
}
