using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The deterministic fallback / test-double classifier behind the
/// <see cref="ISignalQualityClassifier"/> seam. Each branch is exercised with a
/// hand-built feature vector so the rule boundaries are pinned independently of
/// the extractor.
/// </summary>
public sealed class HeuristicSignalQualityClassifierTests
{
    private static SignalQualityFeatures Features(
        float snrDb = 40f,
        float margin = 5f,
        float noiseFloor = 0.01f,
        float jitterCv = 0f,
        float peakCv = 0f,
        float missedRate = 0f,
        float syncLossRate = 0f,
        float syncedFraction = 1f)
        => new SignalQualityFeatures(
            snrDb, margin, noiseFloor, jitterCv, peakCv, missedRate, syncLossRate, syncedFraction);

    private static SignalQualityClass Classify(SignalQualityFeatures f)
        => new HeuristicSignalQualityClassifier().Classify(f).Class;

    [Fact]
    public void CleanFeaturesClassifyGood()
    {
        Assert.Equal(SignalQualityClass.Good, Classify(Features()));
    }

    [Fact]
    public void ModerateSnrClassifiesNoisy()
    {
        Assert.Equal(SignalQualityClass.Noisy, Classify(Features(snrDb: 20f)));
    }

    [Fact]
    public void InconsistentPeaksClassifyNoisy()
    {
        Assert.Equal(SignalQualityClass.Noisy, Classify(Features(peakCv: 0.3f)));
    }

    [Fact]
    public void LowSnrClassifiesWeakSignal()
    {
        Assert.Equal(SignalQualityClass.WeakSignal, Classify(Features(snrDb: 8f)));
    }

    [Fact]
    public void ThinMarginClassifiesWeakSignal()
    {
        Assert.Equal(SignalQualityClass.WeakSignal, Classify(Features(margin: 1.2f)));
    }

    [Fact]
    public void MissedBeatsClassifyUnstable()
    {
        Assert.Equal(SignalQualityClass.Unstable, Classify(Features(missedRate: 0.1f)));
    }

    [Fact]
    public void HighJitterClassifiesUnstable()
    {
        Assert.Equal(SignalQualityClass.Unstable, Classify(Features(jitterCv: 0.1f)));
    }

    [Fact]
    public void SyncLossClassifiesUnstable()
    {
        Assert.Equal(SignalQualityClass.Unstable, Classify(Features(syncLossRate: 0.05f)));
    }

    [Fact]
    public void PoorSyncFractionClassifiesUnknown()
    {
        Assert.Equal(SignalQualityClass.Unknown, Classify(Features(syncedFraction: 0.2f)));
    }

    [Fact]
    public void PreSyncTakesPrecedenceOverOtherFaults()
    {
        // Not synced yet: we can't trust any verdict, even with obvious faults.
        Assert.Equal(
            SignalQualityClass.Unknown,
            Classify(Features(snrDb: 8f, missedRate: 0.1f, syncedFraction: 0.2f)));
    }

    [Fact]
    public void AssessmentEchoesFeaturesAndBoundedConfidence()
    {
        SignalQualityFeatures f = Features();
        SignalQualityAssessment a = new HeuristicSignalQualityClassifier().Classify(f);

        Assert.Equal(f, a.Features);
        Assert.InRange(a.Confidence, 0f, 1f);
        Assert.True(a.Confidence > 0f);
    }

    [Fact]
    public void UnknownAssessmentDefaultIsUnknownClassZeroConfidence()
    {
        Assert.Equal(SignalQualityClass.Unknown, SignalQualityAssessment.Unknown.Class);
        Assert.Equal(0f, SignalQualityAssessment.Unknown.Confidence);
    }

    // Threshold boundaries — pin the constants so a change is a deliberate, tested edit.

    [Fact]
    public void MissedRateJustBelowThresholdStaysGood()
    {
        // 0.04 < UnstableMissedRate (0.05) -> not Unstable; otherwise clean -> Good.
        Assert.Equal(SignalQualityClass.Good, Classify(Features(missedRate: 0.04f)));
    }

    [Fact]
    public void MissedRateJustAboveThresholdIsUnstable()
    {
        Assert.Equal(SignalQualityClass.Unstable, Classify(Features(missedRate: 0.06f)));
    }

    [Fact]
    public void IntervalJitterJustBelowThresholdStaysGood()
    {
        // 0.04 < UnstableIntervalCv (0.05) -> not Unstable.
        Assert.Equal(SignalQualityClass.Good, Classify(Features(jitterCv: 0.04f)));
    }

    [Fact]
    public void SnrJustBelowNoisyThresholdIsNoisy()
    {
        // 23 dB < NoisySnrDb (24) but >= WeakSnrDb (12) -> Noisy, not Weak.
        Assert.Equal(SignalQualityClass.Noisy, Classify(Features(snrDb: 23f)));
    }

    [Fact]
    public void SnrJustAboveNoisyThresholdStaysGood()
    {
        // 25 dB >= NoisySnrDb (24) with a healthy margin and consistent peaks -> Good.
        Assert.Equal(SignalQualityClass.Good, Classify(Features(snrDb: 25f)));
    }
}
