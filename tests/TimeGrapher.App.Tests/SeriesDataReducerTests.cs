using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SeriesDataReducerTests
{
    [Fact]
    public void FindSeriesReturnsTheFirstMatchById()
    {
        var first = new GraphSeriesFrame { Id = AnalysisGraphSeries.RateTic };
        var duplicate = new GraphSeriesFrame { Id = AnalysisGraphSeries.RateTic };
        var other = new GraphSeriesFrame { Id = AnalysisGraphSeries.RateToc };

        Assert.Same(
            first,
            SeriesDataReducer.FindSeries(new[] { other, first, duplicate }, AnalysisGraphSeries.RateTic));
    }

    [Fact]
    public void FindSeriesReturnsNullWhenTheIdIsAbsent()
    {
        var seriesList = new[] { new GraphSeriesFrame { Id = AnalysisGraphSeries.RateTic } };

        Assert.Null(SeriesDataReducer.FindSeries(seriesList, AnalysisGraphSeries.ScopePcm));
    }

    [Fact]
    public void ReplaceSeriesDataDecimatesToPointBudget()
    {
        var targetX = new List<double>();
        var targetY = new List<double>();
        var sourceX = Enumerable.Range(0, 10).Select(value => (double)value).ToArray();
        var sourceY = sourceX.Select(value => value * 10.0).ToArray();

        SeriesDataReducer.ReplaceSeriesData(targetX, targetY, sourceX, sourceY, targetPointBudget: 3);

        Assert.Equal(new[] { 0.0, 4.0, 8.0 }, targetX);
        Assert.Equal(new[] { 0.0, 40.0, 80.0 }, targetY);
    }

    [Fact]
    public void TryReplaceSeriesDataRejectsAppendPayload()
    {
        var series = new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.ScopePcm,
            X = new List<double> { 1.0 },
            Y = new List<double> { 2.0 },
            Replace = false,
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            SeriesDataReducer.TryReplaceSeriesData(series, new List<double>(), new List<double>(), targetPointBudget: 10));

        Assert.Equal("Graph series 'scope.pcm' must be a replace snapshot.", ex.Message);
    }

    [Fact]
    public void ReplaceSeriesDataUsesShortestCoordinateList()
    {
        var targetX = new List<double>();
        var targetY = new List<double>();

        SeriesDataReducer.ReplaceSeriesData(
            targetX,
            targetY,
            new[] { 1.0, 2.0, 3.0 },
            new[] { 10.0, 20.0 },
            targetPointBudget: 10);

        Assert.Equal(new[] { 1.0, 2.0 }, targetX);
        Assert.Equal(new[] { 10.0, 20.0 }, targetY);
    }

    [Fact]
    public void TryReplaceSeriesDataPeak_PreservesPerBinMaximum()
    {
        // 10 source points, budget 5 -> bin size 2. A spike at index 3 (the second
        // sample of bin [2,3]) must survive; plain bin-start subsampling would drop it.
        var series = new GraphSeriesFrame
        {
            Id = "filter.f0",
            X = new List<double> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            Y = new List<double> { 0, 0, 0, 9, 0, 0, 0, 0, 0, 0 },
            Replace = true,
        };
        var targetX = new List<double>();
        var targetY = new List<double>();

        bool ok = SeriesDataReducer.TryReplaceSeriesDataPeak(series, targetX, targetY, targetPointBudget: 5);

        Assert.True(ok);
        // Consistent bin-start X so every lane bins the same source positions.
        Assert.Equal(new[] { 0.0, 2.0, 4.0, 6.0, 8.0 }, targetX);
        // The spike survives peak decimation in its bin; the rest stay flat.
        Assert.Equal(new[] { 0.0, 9.0, 0.0, 0.0, 0.0 }, targetY);
    }

    [Fact]
    public void TryReplaceSeriesDataPeak_CopiesAllWhenUnderBudget()
    {
        var series = new GraphSeriesFrame
        {
            Id = "filter.f0",
            X = new List<double> { 0, 1, 2 },
            Y = new List<double> { 4, 5, 6 },
            Replace = true,
        };
        var targetX = new List<double>();
        var targetY = new List<double>();

        bool ok = SeriesDataReducer.TryReplaceSeriesDataPeak(series, targetX, targetY, targetPointBudget: 8);

        Assert.True(ok);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, targetX);
        Assert.Equal(new[] { 4.0, 5.0, 6.0 }, targetY);
    }
}
