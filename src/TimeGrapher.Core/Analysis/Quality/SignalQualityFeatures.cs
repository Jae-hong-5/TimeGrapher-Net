namespace TimeGrapher.Core.Analysis.Quality;

/// <summary>
/// The numeric feature vector summarising recent signal health, derived only
/// from values the detector/metrics pipeline already computes (no new DSP).
///
/// This is the single contract shared by the live classifier and the offline
/// trainer, so the model can never be fed a feature layout that differs from
/// what runs at measurement time (the same "shared engine contract" discipline
/// used by <c>DetectorMetricsEngine</c>). The ML implementation lives outside
/// Core; Core only defines the shape.
/// </summary>
/// <param name="SnrDb">20·log10(referencePeak / noiseFloor): tick-to-background ratio in dB.</param>
/// <param name="PeakMarginRatio">referencePeak / minPeakThreshold: how far the reference tick sits above the accept floor.</param>
/// <param name="NoiseFloorLevel">Absolute envelope noise floor.</param>
/// <param name="IntervalJitterCv">Coefficient of variation of recent A-A intervals (timing jitter).</param>
/// <param name="PeakLevelCv">Coefficient of variation of recent A-event peak values (amplitude consistency).</param>
/// <param name="MissedBeatRate">Missed beats accumulated over the window, per A event.</param>
/// <param name="SyncLossRate">Sync losses accumulated over the window, per A event.</param>
/// <param name="SyncedFraction">Fraction of recent observations that were synced (0..1).</param>
public readonly record struct SignalQualityFeatures(
    float SnrDb,
    float PeakMarginRatio,
    float NoiseFloorLevel,
    float IntervalJitterCv,
    float PeakLevelCv,
    float MissedBeatRate,
    float SyncLossRate,
    float SyncedFraction);
