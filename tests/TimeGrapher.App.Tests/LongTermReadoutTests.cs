using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure logic behind the Long-Term Performance readouts: the verdict, elapsed
/// time tick formatting, and the review-cursor metrics.
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

    [Theory]
    [InlineData(59.0, "00:59")]
    [InlineData(3720.0, "01:02")]
    [InlineData(86400.0, "24:00")]
    public void FormatElapsedTick_SwitchesToHourMinuteForLongRuns(double seconds, string expected)
    {
        Assert.Equal(expected, LongTermReadout.FormatElapsedTick(seconds));
    }

    [Fact]
    public void ElapsedTicks_UsesSixHourMarksForDayScale()
    {
        (double[] values, string[] labels) = LongTermReadout.ElapsedTicks(0.0, 24 * 60 * 60);

        Assert.Equal(new[] { 0.0, 21600.0, 43200.0, 64800.0, 86400.0 }, values);
        Assert.Equal(new[] { "00:00", "06:00", "12:00", "18:00", "24:00" }, labels);
    }

    [Fact]
    public void ElapsedTicks_AvoidsRepeatedLabelsForShortWindows()
    {
        (_, string[] labels) = LongTermReadout.ElapsedTicks(2.0, 6.0);

        Assert.Equal(new[] { "00:02", "00:03", "00:04", "00:05", "00:06" }, labels);
    }

    [Fact]
    public void ReviewMetrics_UsesLiveValuesOrCursorSeriesValues()
    {
        var history = new BeatMetricsHistorySnapshot
        {
            Rate = Series(new[] { 0.0, 10.0, 20.0 }, new[] { -1.0, -2.0, -3.0 }),
            Amplitude = Series(new[] { 0.0, 10.0, 20.0 }, new[] { 280.0, 281.0, 282.0 }),
            BeatError = Series(new[] { 0.0, 10.0, 20.0 }, new[] { 0.1, 0.2, 0.3 }),
            RateValid = true,
            RateSPerDay = -3.0,
            AmplitudeValid = true,
            AmplitudeDeg = 282.0,
            BeatErrorValid = true,
            BeatErrorSignedMs = 0.3,
        };

        Assert.Equal(
            "Error Rate -3.0 s/d   Amplitude 282°   BEAT ERROR +0.3 ms",
            LongTermReadout.ReviewMetrics(history, null));
        Assert.Equal(
            "Error Rate -2.0 s/d   Amplitude 281°   BEAT ERROR +0.2 ms",
            LongTermReadout.ReviewMetrics(history, 12.0));
    }

    [Fact]
    public void Verdict_ChecksWholeSeriesAgainstToleranceCorridors()
    {
        var okHistory = new BeatMetricsHistorySnapshot
        {
            Rate = Series(new[] { 0.0 }, new[] { 0.0 }),
            Amplitude = Series(new[] { 0.0 }, new[] { 282.0 }),
            BeatError = Series(new[] { 0.0 }, new[] { 0.1 }),
        };
        var badHistory = new BeatMetricsHistorySnapshot
        {
            // Out of range past the warmup window, so it is judged.
            Rate = Series(new[] { 0.0, 60.0 }, new[] { 0.0, 12.0 }),
            Amplitude = Series(new[] { 0.0, 60.0 }, new[] { 282.0, 282.0 }),
            BeatError = Series(new[] { 0.0, 60.0 }, new[] { 0.1, 0.1 }),
        };

        Assert.Equal("IN TOLERANCE", LongTermReadout.Verdict(okHistory));
        Assert.Equal("CHECK", LongTermReadout.Verdict(badHistory));
        Assert.Equal("COLLECTING", LongTermReadout.Verdict(new BeatMetricsHistorySnapshot()));
    }

    [Fact]
    public void Verdict_ExcludesWarmupSpikeFromToleranceCheck()
    {
        // A large startup transient inside the first 30 s must not fail the run;
        // once a settled bucket exists past the warmup window it judges that.
        var history = new BeatMetricsHistorySnapshot
        {
            Rate = Series(new[] { 0.0, 10.0, 3600.0 }, new[] { 50.0, 40.0, 1.0 }),
            Amplitude = Series(new[] { 0.0, 10.0, 3600.0 }, new[] { 282.0, 282.0, 282.0 }),
            BeatError = Series(new[] { 0.0, 10.0, 3600.0 }, new[] { 0.1, 0.1, 0.1 }),
        };

        Assert.Equal("IN TOLERANCE", LongTermReadout.Verdict(history));
    }
}
