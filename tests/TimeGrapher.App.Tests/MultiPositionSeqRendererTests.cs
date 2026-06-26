using System;
using System.Linq;
using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MultiPositionSeqRendererTests
{
    [Fact]
    public void RenderFrame_MarksBeatErrorAgainstCurrentAcceptBand()
    {
        var grid = new Grid();
        var renderer = new MultiPositionSeqRenderer(grid, Dashboard(), WatchPosition.CH);
        AcceptBandSettings original = AcceptBandSettings.Current;

        try
        {
            AcceptBandSettings.Current = AcceptBandSettings.Default with { BeatErrorMagnitudeMs = 0.5 };
            renderer.RenderFrame(Frame(1, WatchPosition.CH, Position(WatchPosition.CH, beatError: 0.7)));

            Assert.True(Cell(grid, WatchPosition.CH, column: 3).IsSet(TextBlock.ForegroundProperty));

            AcceptBandSettings.Current = AcceptBandSettings.Default with { BeatErrorMagnitudeMs = 1.0 };
            renderer.ApplyAcceptBands();

            Assert.False(Cell(grid, WatchPosition.CH, column: 3).IsSet(TextBlock.ForegroundProperty));
        }
        finally
        {
            AcceptBandSettings.Current = original;
        }
    }

    [Fact]
    public void RateRangeLaneAxisIncludesEditedBandAndMeasuredExtremes()
    {
        AcceptBandSettings original = AcceptBandSettings.Current;

        try
        {
            AcceptBandSettings.Current = AcceptBandSettings.Default with
            {
                RateMinSPerDay = -90.0,
                RateMaxSPerDay = 80.0,
            };

            (double bandMin, double bandMax) = RateRangeLaneControl.AxisRange(
                hasValue: false, min: 0.0, mean: 0.0, max: 0.0);
            Assert.True(bandMin < -90.0);
            Assert.True(bandMax > 80.0);

            (double dataMin, double dataMax) = RateRangeLaneControl.AxisRange(
                hasValue: true, min: -110.0, mean: 0.0, max: 115.0);
            Assert.True(dataMin < -110.0);
            Assert.True(dataMax > 115.0);
        }
        finally
        {
            AcceptBandSettings.Current = original;
        }
    }

    [Fact]
    public void WatchPositionsFrameConsumerReceivesAcceptBandUpdates()
    {
        Assert.True(typeof(IAcceptBandConsumer).IsAssignableFrom(typeof(WatchPositionsFrameConsumer)));
    }

    private static PositionSequenceDashboardControls Dashboard() => new(
        new TextBlock(),
        new TextBlock(),
        new TextBlock(),
        new TextBlock(),
        new Grid(),
        new Border(),
        new TextBlock(),
        new TextBlock(),
        new TextBlock(),
        new TextBlock(),
        new TextBlock());

    private static AnalysisFrame Frame(ulong version, WatchPosition activePosition, params PositionSummary[] positions) => new()
    {
        MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = version,
            ActivePosition = activePosition,
            Positions = positions,
        },
    };

    private static PositionSummary Position(
        WatchPosition position,
        double? rate = null,
        double? amplitude = null,
        double? beatError = null) => new(
        position,
        rate is double r ? Stats(r) : default,
        amplitude is double a ? Stats(a) : default,
        beatError is double b ? Stats(b) : default);

    private static StatsSummary Stats(double mean) =>
        new(Valid: true, Min: mean, Max: mean, Mean: mean, Sigma: 0.0, Count: 10);

    private static TextBlock Cell(Grid grid, WatchPosition position, int column)
    {
        int row = WatchPositions.All
            .Select((candidate, index) => new { candidate, index })
            .Single(item => item.candidate == position)
            .index + 1;

        return grid.Children
            .OfType<TextBlock>()
            .Single(cell => Grid.GetRow(cell) == row && Grid.GetColumn(cell) == column);
    }
}
