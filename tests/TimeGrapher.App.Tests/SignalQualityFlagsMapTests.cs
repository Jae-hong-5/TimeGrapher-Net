using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The reconciliation table mapping the trained classifier's window-level verdict
/// onto the per-beat SignalQualityFlags vocabulary. The model contributes only the
/// degraded-health bits; tentative (low-confidence) and Good/Unknown verdicts map
/// to None.
/// </summary>
public sealed class SignalQualityFlagsMapTests
{
    private static SignalQualityAssessment Assess(SignalQualityClass cls, float confidence = 0.9f)
        => new(cls, confidence, default);

    [Fact]
    public void NullAssessmentMapsToNone()
    {
        Assert.Equal(SignalQualityFlags.None, SignalQualityFlagsMap.From(null));
    }

    [Fact]
    public void GoodAndUnknownMapToNone()
    {
        Assert.Equal(SignalQualityFlags.None, SignalQualityFlagsMap.From(Assess(SignalQualityClass.Good)));
        Assert.Equal(SignalQualityFlags.None, SignalQualityFlagsMap.From(Assess(SignalQualityClass.Unknown)));
    }

    [Fact]
    public void NoisyMapsToNoisySignal()
    {
        Assert.Equal(SignalQualityFlags.NoisySignal, SignalQualityFlagsMap.From(Assess(SignalQualityClass.Noisy)));
    }

    [Fact]
    public void WeakSignalMapsToWeakSignal()
    {
        Assert.Equal(SignalQualityFlags.WeakSignal, SignalQualityFlagsMap.From(Assess(SignalQualityClass.WeakSignal)));
    }

    [Fact]
    public void UnstableMapsToCTimingUnstable()
    {
        Assert.Equal(SignalQualityFlags.CTimingUnstable, SignalQualityFlagsMap.From(Assess(SignalQualityClass.Unstable)));
    }

    [Fact]
    public void BelowConfidenceFloorMapsToNone()
    {
        SignalQualityAssessment tentative = Assess(SignalQualityClass.Noisy, SignalQualityFlagsMap.ConfidenceFloor - 0.01f);
        Assert.Equal(SignalQualityFlags.None, SignalQualityFlagsMap.From(tentative));
    }
}
