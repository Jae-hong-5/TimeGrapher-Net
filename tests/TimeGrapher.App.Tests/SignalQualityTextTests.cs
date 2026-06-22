using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SignalQualityTextTests
{
    [Fact]
    public void SummaryPrioritizesActionableSignalQualityCause()
    {
        Assert.Equal("No signal", SignalQualityText.Summary(SignalQualityFlags.NoSignal | SignalQualityFlags.WeakSignal));
        Assert.Equal("Possible false C", SignalQualityText.Summary(SignalQualityFlags.CTimingUnstable | SignalQualityFlags.PossibleFalseC));
        Assert.Equal("Noisy signal", SignalQualityText.Summary(SignalQualityFlags.NoisySignal));
        Assert.Equal("Weak signal", SignalQualityText.Summary(SignalQualityFlags.WeakSignal));
        Assert.Equal(string.Empty, SignalQualityText.Summary(SignalQualityFlags.None));
    }

    [Fact]
    public void GuidanceGivesRecoveryAction()
    {
        Assert.Contains("Reposition", SignalQualityText.Guidance(SignalQualityFlags.WeakSignal));
        Assert.Contains("Reduce", SignalQualityText.Guidance(SignalQualityFlags.NoisySignal));
        Assert.Contains("Beat Noise", SignalQualityText.Guidance(SignalQualityFlags.PossibleFalseC));
    }
}
