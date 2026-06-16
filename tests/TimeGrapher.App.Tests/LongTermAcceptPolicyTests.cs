using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The Long-Term acceptable-range corridor must alias the bands the other
/// displays already use, so every view judges "in tolerance" against the same
/// numbers (consistency driver, QAS-4).
/// </summary>
public sealed class LongTermAcceptPolicyTests
{
    [Fact]
    public void Rate_AliasesVarioGaugeBand()
    {
        Assert.Equal(VarioGaugePolicy.RateAcceptMinSPerDay, LongTermAcceptPolicy.Rate.Min);
        Assert.Equal(VarioGaugePolicy.RateAcceptMaxSPerDay, LongTermAcceptPolicy.Rate.Max);
    }

    [Fact]
    public void Amplitude_AliasesTraceNormalRange()
    {
        Assert.Equal(TraceAlertEvaluator.AmplitudeMinDeg, LongTermAcceptPolicy.Amplitude.Min);
        Assert.Equal(TraceAlertEvaluator.AmplitudeMaxDeg, LongTermAcceptPolicy.Amplitude.Max);
    }

    [Fact]
    public void BeatError_IsSymmetricAboutZeroAtDiagnosticThreshold()
    {
        Assert.Equal(-BeatErrorDiagnostics.SeparationAlertThresholdMs, LongTermAcceptPolicy.BeatError.Min);
        Assert.Equal(BeatErrorDiagnostics.SeparationAlertThresholdMs, LongTermAcceptPolicy.BeatError.Max);
    }

    [Fact]
    public void EveryCorridor_HasMinBelowMax()
    {
        Assert.True(LongTermAcceptPolicy.Rate.Min < LongTermAcceptPolicy.Rate.Max);
        Assert.True(LongTermAcceptPolicy.Amplitude.Min < LongTermAcceptPolicy.Amplitude.Max);
        Assert.True(LongTermAcceptPolicy.BeatError.Min < LongTermAcceptPolicy.BeatError.Max);
    }
}
