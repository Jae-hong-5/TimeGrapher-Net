using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure logic behind the Trace Display tab: summary math over decimated series
/// and the project-plan alert policy (running late; amplitude outside the default 270-300°).
/// </summary>
public sealed class TraceDisplayLogicTests
{
    private static MetricsHistorySeries Series(double[] x, double[] y) => new()
    {
        X = x,
        Y = y,
        YMin = y,
        YMax = y,
    };

    private static BeatMetricsHistorySnapshot Snapshot(
        bool rateValid = false, double rate = 0.0,
        bool amplitudeValid = false, double amplitude = 285.0) => new()
        {
            RateValid = rateValid,
            RateSPerDay = rate,
            AmplitudeValid = amplitudeValid,
            AmplitudeDeg = amplitude,
        };

    [Fact]
    public void Average_IsMeanOfStoredPoints()
    {
        MetricsHistorySeries series = Series(new[] { 1.0, 2.0, 3.0 }, new[] { 10.0, 20.0, 30.0 });
        Assert.Equal(20.0, MetricsSeriesMath.Average(series));
        Assert.Null(MetricsSeriesMath.Average(MetricsHistorySeries.Empty));
    }

    [Fact]
    public void Alert_FiresWhenWatchRunsLate()
    {
        TraceAlertState state = TraceAlertEvaluator.Evaluate(Snapshot(rateValid: true, rate: -12.3));

        Assert.True(state.RateSlow);
        Assert.False(state.AmplitudeOutOfRange);
        Assert.Equal("Watch is running late (-12.3 s/d)", state.Message);
    }

    [Fact]
    public void Alert_DeadbandKeepsHealthyWatchQuiet()
    {
        // Slightly slow but inside the -1 s/d deadband: no alert flicker.
        TraceAlertState state = TraceAlertEvaluator.Evaluate(Snapshot(rateValid: true, rate: -0.5));
        Assert.False(state.RateSlow);
        Assert.Null(state.Message);
    }

    [Theory]
    [InlineData(269.0, true)]
    [InlineData(270.0, false)]
    [InlineData(300.0, false)]
    [InlineData(301.0, true)]
    public void Alert_AmplitudeBandIs270To300Inclusive(double amplitude, bool expectAlert)
    {
        TraceAlertState state = TraceAlertEvaluator.Evaluate(
            Snapshot(amplitudeValid: true, amplitude: amplitude));

        Assert.Equal(expectAlert, state.AmplitudeOutOfRange);
    }

    [Fact]
    public void Alert_CombinesBothConditionsInOneMessage()
    {
        TraceAlertState state = TraceAlertEvaluator.Evaluate(
            Snapshot(rateValid: true, rate: -5.0, amplitudeValid: true, amplitude: 240.0));

        Assert.True(state.RateSlow);
        Assert.True(state.AmplitudeOutOfRange);
        Assert.Equal("Watch is running late (-5.0 s/d)  |  Amplitude 240° outside normal range 270–300°", state.Message);
    }

    [Fact]
    public void Alert_InvalidReadingsNeverAlert()
    {
        TraceAlertState state = TraceAlertEvaluator.Evaluate(
            Snapshot(rateValid: false, rate: -99.0, amplitudeValid: false, amplitude: 100.0));

        Assert.False(state.RateSlow);
        Assert.False(state.AmplitudeOutOfRange);
        Assert.Null(state.Message);
    }
}
