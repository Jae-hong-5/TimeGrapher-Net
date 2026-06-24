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

    [Fact]
    public void OverlayStateFadesAfterConsecutiveCleanSignals()
    {
        var state = new SignalQualityOverlayState();

        Assert.True(state.Update(SignalQualityFlags.PossibleFalseC, out string warning, out byte alpha));
        Assert.Equal("POSSIBLE FALSE C", warning);
        Assert.Equal(byte.MaxValue, alpha);

        for (int i = 0; i < SignalQualityOverlayState.FadeStartCleanCount; i++)
        {
            Assert.True(state.Update(SignalQualityFlags.None, out warning, out alpha));
            Assert.Equal("POSSIBLE FALSE C", warning);
            Assert.Equal(byte.MaxValue, alpha);
        }

        Assert.True(state.Update(SignalQualityFlags.None, out warning, out alpha));
        Assert.Equal("POSSIBLE FALSE C", warning);
        Assert.True(alpha < byte.MaxValue);

        for (int i = SignalQualityOverlayState.FadeStartCleanCount + 2; i < SignalQualityOverlayState.HideCleanCount; i++)
        {
            Assert.True(state.Update(SignalQualityFlags.None, out _, out _));
        }

        Assert.False(state.Update(SignalQualityFlags.None, out warning, out alpha));
        Assert.Equal(string.Empty, warning);
        Assert.Equal(0, alpha);
    }
}