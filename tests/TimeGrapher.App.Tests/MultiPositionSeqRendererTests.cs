using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MultiPositionSeqRendererTests
{
    private const double TableFontSizeForTest = 15.0;

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
    public void RateRangeLaneAcceptBandUsesLongTermBeatErrorGreen()
    {
        var palette = new PlotThemePalette(
            SurfaceBg: 0,
            ScopeBg: 0,
            ScopeGrid: 0,
            TextPrimary: 0,
            TraceWave: 0,
            TraceTick: 0xFF2C9118,
            TraceTock: 0);

        Assert.Equal(0x2A2C9118u, RateRangeLaneControl.AcceptBandFillArgb(palette));
    }

    [Fact]
    public void RateRangeLaneTrackUsesCollectionDefaultGray()
    {
        var palette = new PlotThemePalette(
            SurfaceBg: 0,
            ScopeBg: 0,
            ScopeGrid: 0,
            TextPrimary: 0,
            TraceWave: 0,
            TraceTick: 0,
            TraceTock: 0,
            ChromeBorder: 0xFFCFCFCF);

        Assert.Equal(palette.ChromeBorder, RateRangeLaneControl.TrackFillArgb(palette));
    }

    [Fact]
    public void RebuildTable_CentersHeadersValuesAndRenamesRateRangeColumn()
    {
        var grid = new Grid();
        var renderer = new MultiPositionSeqRenderer(grid, Dashboard(), WatchPosition.CH);

        renderer.RenderFrame(Frame(
            1,
            WatchPosition.CH,
            Position(WatchPosition.CH, rate: 1.2, amplitude: 280.0, beatError: 0.03)));

        Border[] headerCells = grid.Children
            .OfType<Border>()
            .Where(cell => Grid.GetRow(cell) == 0)
            .ToArray();
        TextBlock[] headers = headerCells
            .Select(cell => Assert.IsType<TextBlock>(cell.Child))
            .ToArray();

        Assert.Equal(7, headerCells.Length);
        Assert.All(headerCells, header =>
        {
            Assert.Equal(new Thickness(0, 0, 0, 4), header.Margin);
            Assert.Equal(new Thickness(8, 2, 8, 2), header.Padding);
            Assert.True(header.IsSet(Border.BackgroundProperty));
        });

        Assert.All(headers, header =>
        {
            Assert.Equal(HorizontalAlignment.Stretch, header.HorizontalAlignment);
            Assert.Equal(TextAlignment.Center, header.TextAlignment);
            Assert.Equal(TableFontSizeForTest, header.FontSize);
            Assert.True(header.IsSet(TextBlock.ForegroundProperty));
        });
        Assert.Contains(headers, header => header.Text == "Pos.");
        Assert.DoesNotContain(headers, header => header.Text == "Position");
        Assert.Contains(headers, header => header.Text == "Rate Range");
        Assert.DoesNotContain(headers, header => header.Text == "Error Rate vs Band");

        TextBlock[] valueCells = grid.Children
            .OfType<TextBlock>()
            .Where(cell => Grid.GetRow(cell) > 0 && Grid.GetColumn(cell) <= 4)
            .ToArray();
        Assert.NotEmpty(valueCells);
        Assert.All(valueCells, cell =>
        {
            Assert.Equal(HorizontalAlignment.Stretch, cell.HorizontalAlignment);
            Assert.Equal(TextAlignment.Center, cell.TextAlignment);
            Assert.Equal(TableFontSizeForTest, cell.FontSize);
        });
    }

    [Fact]
    public void RebuildTable_CollectionCellsShowOnlySquareBars()
    {
        var grid = new Grid();
        var renderer = new MultiPositionSeqRenderer(grid, Dashboard(), WatchPosition.CH);

        renderer.RenderFrame(Frame(1, WatchPosition.CH, Position(WatchPosition.CH, rate: 1.0)));

        var collectionBar = Assert.Single(grid.Children.OfType<Border>(),
            child => Grid.GetRow(child) == 1 && Grid.GetColumn(child) == 6);
        Assert.Equal(new CornerRadius(0), collectionBar.CornerRadius);
        Assert.IsType<Grid>(collectionBar.Child);
        Assert.DoesNotContain(grid.Children.OfType<TextBlock>(),
            text => Grid.GetRow(text) > 0 && Grid.GetColumn(text) == 6);
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
