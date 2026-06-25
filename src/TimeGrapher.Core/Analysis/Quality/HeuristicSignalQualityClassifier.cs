using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis.Quality;

/// <summary>
/// A deterministic, dependency-free threshold classifier. Its role is not to be
/// the shipped "AI" feature but to act as the fallback / test double behind the
/// <see cref="ISignalQualityClassifier"/> seam: it lets the whole pipeline be
/// exercised without loading the ML.NET model, and gives a sane verdict if no
/// model is available. The trained model replaces it at the same seam.
///
/// Thresholds are intentionally coarse round numbers, not tuned constants; the
/// trained classifier learns the real boundaries from labelled synthetic signals.
/// They are checked in a fixed precedence (Unknown -> Unstable -> Weak -> Noisy
/// -> Good) so the most serious condition wins, and the boundaries are pinned by
/// HeuristicSignalQualityClassifierTests. The confidence values are likewise
/// coarse priors (instability is rarely a false positive, so it is the most
/// confident; Noisy is the least), and stay above SignalQualityFlagsMap's 0.5
/// floor so a fallback verdict always surfaces. The trained model replaces both
/// the thresholds and the confidences with learned values at the same seam.
/// </summary>
public sealed class HeuristicSignalQualityClassifier : ISignalQualityClassifier
{
    // Timing instability (any one trips Unstable). Rates are per-window-beat.
    private const float UnstableMissedRate = 0.05f;   // >5% of window beats missed
    private const float UnstableSyncLossRate = 0.02f; // >2% of the window lost sync
    private const float UnstableIntervalCv = 0.05f;   // >5% A-A interval jitter

    // Weak signal: tick energy near the noise floor / barely above the accept floor.
    private const float WeakSnrDb = 12.0f;            // tick-to-noise below ~12 dB
    private const float WeakPeakMargin = 1.5f;        // reference peak < 1.5x accept threshold

    // Noisy: usable but degraded (moderate SNR, or inconsistent peak amplitudes).
    private const float NoisySnrDb = 24.0f;           // SNR in the ~12-24 dB band
    private const float NoisyPeakCv = 0.25f;          // >25% peak-amplitude variation

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
