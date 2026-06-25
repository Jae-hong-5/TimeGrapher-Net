using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// AcceptBandSettings is the single source every graph reads its normal band from.
/// These tests pin the historical defaults and prove that replacing Current moves
/// every per-measure policy at once, so all displays judge "in tolerance" against
/// the same numbers (consistency driver, QAS-4). The assembly runs test
/// collections serially (xunit.runner.json), so swapping the global Current and
/// restoring it in a finally is race-free.
/// </summary>
public sealed class AcceptBandSettingsTests
{
    [Fact]
    public void Default_MatchesIndustryReferenceBands()
    {
        // Defaults follow common industry references: rate -4/+6 s/d (COSC/ISO 3159
        // chronometer rate), amplitude 270-300 deg (project trace-display normal range), beat
        // error +-0.8 ms (within the acceptable <=1 ms convention).
        AcceptBandSettings d = AcceptBandSettings.Default;
        Assert.Equal(-4.0, d.RateMinSPerDay);
        Assert.Equal(6.0, d.RateMaxSPerDay);
        Assert.Equal(270.0, d.AmplitudeMinDeg);
        Assert.Equal(300.0, d.AmplitudeMaxDeg);
        Assert.Equal(0.8, d.BeatErrorMagnitudeMs);
        Assert.True(d.IsValid);
    }

    [Theory]
    [InlineData(-10.0, 10.0, 270.0, 300.0, 0.6, true)]
    [InlineData(10.0, 10.0, 270.0, 300.0, 0.6, false)]   // rate min == max
    [InlineData(-10.0, 10.0, 300.0, 270.0, 0.6, false)]  // amplitude inverted
    [InlineData(-10.0, 10.0, 270.0, 300.0, 0.0, false)]  // beat-error magnitude not positive
    public void IsValid_RequiresOrderedBandsAndPositiveBeatError(
        double rateMin, double rateMax, double ampMin, double ampMax, double beatErr, bool expected)
    {
        var settings = new AcceptBandSettings(rateMin, rateMax, ampMin, ampMax, beatErr);
        Assert.Equal(expected, settings.IsValid);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e29)]   // finite but overflows decimal and is far outside the editable range
    public void IsValid_RejectsNonFiniteOrOutOfRangeValues(double bad)
    {
        // A hand-edited/corrupt file must not load a band the Settings UI cannot
        // represent — and must not feed a value that overflows the decimal cast the
        // inputs use (which would crash startup).
        Assert.False(new AcceptBandSettings(bad, 10.0, 270.0, 300.0, 0.6).IsValid);
        Assert.False(new AcceptBandSettings(-10.0, bad, 270.0, 300.0, 0.6).IsValid);
        Assert.False(new AcceptBandSettings(-10.0, 10.0, bad, 300.0, 0.6).IsValid);
        Assert.False(new AcceptBandSettings(-10.0, 10.0, 270.0, bad, 0.6).IsValid);
        Assert.False(new AcceptBandSettings(-10.0, 10.0, 270.0, 300.0, bad).IsValid);
    }

    [Fact]
    public void ShouldReplace_AppliesOnlyValidDifferentBands()
    {
        var current = AcceptBandSettings.Default;

        Assert.False(current.ShouldReplace(current));                                   // no-op
        Assert.False(current.ShouldReplace(current with { RateMinSPerDay = 20.0 }));    // inverted -> invalid
        Assert.False(current.ShouldReplace(current with { BeatErrorMagnitudeMs = 0.0 })); // out of range
        Assert.True(current.ShouldReplace(current with { AmplitudeMinDeg = 250.0 }));   // valid + different
    }

    [Fact]
    public void ReplacingCurrent_MovesEveryPerMeasurePolicy()
    {
        AcceptBandSettings original = AcceptBandSettings.Current;
        try
        {
            AcceptBandSettings.Current = new AcceptBandSettings(
                RateMinSPerDay: -7.0,
                RateMaxSPerDay: 5.0,
                AmplitudeMinDeg: 250.0,
                AmplitudeMaxDeg: 310.0,
                BeatErrorMagnitudeMs: 1.2);

            // Rate band → Vario gauge policy and the Long-Term corridor.
            Assert.Equal(-7.0, VarioGaugePolicy.RateAcceptMinSPerDay);
            Assert.Equal(5.0, VarioGaugePolicy.RateAcceptMaxSPerDay);
            Assert.Equal(-7.0, LongTermAcceptPolicy.Rate.Min);
            Assert.Equal(5.0, LongTermAcceptPolicy.Rate.Max);

            // Amplitude band → Trace evaluator, Vario alias and the Long-Term corridor.
            Assert.Equal(250.0, TraceAlertEvaluator.AmplitudeMinDeg);
            Assert.Equal(310.0, TraceAlertEvaluator.AmplitudeMaxDeg);
            Assert.Equal(250.0, VarioGaugePolicy.AmplitudeAcceptMinDeg);
            Assert.Equal(310.0, VarioGaugePolicy.AmplitudeAcceptMaxDeg);
            Assert.Equal(250.0, LongTermAcceptPolicy.Amplitude.Min);
            Assert.Equal(310.0, LongTermAcceptPolicy.Amplitude.Max);

            // Beat-error magnitude → diagnostic threshold and the symmetric corridor.
            Assert.Equal(1.2, BeatErrorDiagnostics.SeparationAlertThresholdMs);
            Assert.Equal(-1.2, LongTermAcceptPolicy.BeatError.Min);
            Assert.Equal(1.2, LongTermAcceptPolicy.BeatError.Max);
        }
        finally
        {
            AcceptBandSettings.Current = original;
        }
    }
}
