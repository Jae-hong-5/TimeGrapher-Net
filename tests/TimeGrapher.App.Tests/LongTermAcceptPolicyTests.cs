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
    // The default-value alias checks (Rate/Amplitude/BeatError aliasing the Vario /
    // Trace / diagnostics bands) were removed as subsumed by
    // AcceptBandSettingsTests.ReplacingCurrent_MovesEveryPerMeasurePolicy, which
    // proves the same aliases hold under MUTATED bands (a strictly stronger check).
    [Fact]
    public void EveryCorridor_HasMinBelowMax()
    {
        AssertCorridorHasMinBelowMax("rate", LongTermAcceptPolicy.Rate);
        AssertCorridorHasMinBelowMax("amplitude", LongTermAcceptPolicy.Amplitude);
        AssertCorridorHasMinBelowMax("beat error", LongTermAcceptPolicy.BeatError);
    }

    private static void AssertCorridorHasMinBelowMax(string name, (double Min, double Max) corridor)
    {
        Assert.True(
            corridor.Min < corridor.Max,
            $"{name} corridor min {corridor.Min} should be below max {corridor.Max}");
    }
}
