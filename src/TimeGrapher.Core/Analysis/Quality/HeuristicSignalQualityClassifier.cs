using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis.Quality;

/// <summary>
/// A deterministic, dependency-free threshold classifier. Its role is not to be
/// the shipped "AI" feature but to act as the fallback / test double behind the
/// <see cref="ISignalQualityClassifier"/> seam: it lets the whole pipeline be
/// exercised without loading the ML.NET model, and gives a sane verdict if no
/// model is available. The trained model replaces it at the same seam.
///
/// Thresholds are intentionally coarse; the trained classifier learns the real
/// boundaries from labelled synthetic signals.
/// </summary>
public sealed class HeuristicSignalQualityClassifier : ISignalQualityClassifier
{
    // Timing instability (any one trips Unstable).
    private const float UnstableMissedRate = 0.05f;   // >5% of window beats missed
    private const float UnstableSyncLossRate = 0.02f;
    private const float UnstableIntervalCv = 0.05f;   // 5% interval jitter

    // Weak signal: tick near the accept floor / low SNR.
    private const float WeakSnrDb = 12.0f;
    private const float WeakPeakMargin = 1.5f;

    // Noisy: usable but degraded.
    private const float NoisySnrDb = 24.0f;
    private const float NoisyPeakCv = 0.25f;

    public SignalQualityAssessment Classify(in SignalQualityFeatures f)
    {
        if (f.SyncedFraction < 0.5f)
        {
            return new SignalQualityAssessment(SignalQualityClass.Unknown, 0f, f);
        }

        if (f.MissedBeatRate > UnstableMissedRate ||
            f.SyncLossRate > UnstableSyncLossRate ||
            f.IntervalJitterCv > UnstableIntervalCv)
        {
            return new SignalQualityAssessment(SignalQualityClass.Unstable, 0.9f, f);
        }

        if (f.SnrDb < WeakSnrDb || f.PeakMarginRatio < WeakPeakMargin)
        {
            return new SignalQualityAssessment(SignalQualityClass.WeakSignal, 0.8f, f);
        }

        if (f.SnrDb < NoisySnrDb || f.PeakLevelCv > NoisyPeakCv)
        {
            return new SignalQualityAssessment(SignalQualityClass.Noisy, 0.7f, f);
        }

        return new SignalQualityAssessment(SignalQualityClass.Good, 0.8f, f);
    }
}
