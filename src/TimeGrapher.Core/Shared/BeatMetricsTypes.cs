namespace TimeGrapher.Core.Shared;

/// <summary>
/// Numerically-typed per-beat measurements. Until these types existed the per-beat
/// values computed by WatchMetrics escaped Core only as formatted strings
/// (ResultsText / CMarkerText), so time-series tabs and derived-measure displays
/// had nothing machine-readable to consume.
/// </summary>

/// <summary>
/// One per-beat timing measurement, emitted with each synced A event.
/// Times are seconds since the start of the audio stream. Bph is the
/// BPH value that the emitting sync segment locked onto; samples are only
/// emitted while synced, so it is always a real lock value (never 0).
/// </summary>
public readonly record struct BeatTimingSample(
    ulong BeatNumber,
    double TimeS,
    bool IsTic,
    double RateErrorMs,
    bool RateValid,
    double RateSPerDay,
    bool BeatErrorValid,
    double BeatErrorSignedMs,
    int Bph);

/// <summary>
/// One per-beat amplitude measurement, emitted with each synced C event.
/// Instant is the single-beat estimate; PairAverage is the tic+toc average that
/// also feeds the rolling display value, updated once per completed pair.
/// </summary>
public readonly record struct AmplitudeSample(
    double TimeS,
    bool InstantValid,
    double InstantDeg,
    bool PairAverageUpdated,
    double PairAverageDeg);

/// <summary>
/// Project-plan derived timing measures (Chour-style watch time parameters):
/// DiffTicTac = tick duration minus tock duration; DiffPeriod = measured-vs-expected
/// beat-duration difference averaged over a short fixed window (4 s); AvgPeriod =
/// the same difference averaged since measurement start (or the last segment
/// restart on a re-lock at a different BPH).
/// </summary>
public readonly record struct DerivedTimingMeasures(
    bool DiffTicTacValid,
    double DiffTicTacMs,
    bool DiffPeriodValid,
    double DiffPeriodMs,
    bool AvgPeriodValid,
    double AvgPeriodMs);
