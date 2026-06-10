using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure logic behind the Long-Term Performance footer: overall averages for the
/// testing period, elapsed time, and the current plot resolution derived from
/// the median spacing of the decimated points.
/// </summary>
public sealed class LongTermReadoutTests
{
    private static MetricsHistorySeries Series(double[] x, double[] y) => new()
    {
        X = x,
        Y = y,
        YMin = y,
        YMax = y,
    };

    [Fact]
    public void MedianPointSpacing_NullUntilTwoPoints()
    {
        Assert.Null(LongTermReadout.MedianPointSpacingS(MetricsHistorySeries.Empty));
        Assert.Null(LongTermReadout.MedianPointSpacingS(Series(new[] { 1.0 }, new[] { 0.0 })));
    }

    [Fact]
    public void MedianPointSpacing_OddSpacingCountTakesMiddleValue()
    {
        // Spacings 1, 2, 4 -> median 2; a single wide sync-loss gap (4) is ignored.
        MetricsHistorySeries series = Series(new[] { 0.0, 1.0, 3.0, 7.0 }, new double[4]);
        Assert.Equal(2.0, LongTermReadout.MedianPointSpacingS(series));
    }

    [Fact]
    public void MedianPointSpacing_EvenSpacingCountAveragesMiddlePair()
    {
        // Spacings 1, 2, 4, 1 -> sorted 1, 1, 2, 4 -> median (1 + 2) / 2.
        MetricsHistorySeries series = Series(new[] { 0.0, 1.0, 3.0, 7.0, 8.0 }, new double[5]);
        Assert.Equal(1.5, LongTermReadout.MedianPointSpacingS(series));
    }

    [Theory]
    [InlineData(0.4, "1 pt ≈ 0.4 s")]
    [InlineData(2.0, "1 pt ≈ 2.0 s")]
    [InlineData(96.0, "1 pt ≈ 96 s")]
    [InlineData(null, "1 pt ≈ —")]
    public void FormatResolution_ShowsSecondsPerPoint(double? secondsPerPoint, string expected)
    {
        Assert.Equal(expected, LongTermReadout.FormatResolution(secondsPerPoint));
    }

    [Fact]
    public void Footer_ShowsAveragesElapsedAndResolution()
    {
        var history = new BeatMetricsHistorySnapshot
        {
            Rate = Series(new[] { 0.0, 2.0, 4.0 }, new[] { 1.0, 2.0, 3.0 }),
            Amplitude = Series(new[] { 0.0, 2.0, 4.0 }, new[] { 280.0, 290.0, 285.0 }),
            BeatError = Series(new[] { 0.0, 2.0, 4.0 }, new[] { 0.5, -0.5, 0.3 }),
            LatestTimeS = 3725.0,
        };

        Assert.Equal(
            "AVG RATE +2.0 s/d   AVG AMP 285°   AVG BEAT ERR +0.10 ms   " +
            "ELAPSED 1:02:05   1 pt ≈ 2.0 s",
            LongTermReadout.Footer(history));
    }

    [Fact]
    public void Footer_ShowsDashesWhileEmpty()
    {
        Assert.Equal(
            "AVG RATE —   AVG AMP —   AVG BEAT ERR —   ELAPSED 00:00   1 pt ≈ —",
            LongTermReadout.Footer(new BeatMetricsHistorySnapshot()));
    }
}
