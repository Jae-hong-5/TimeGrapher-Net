using System;
using System.Text.Json.Serialization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App;

/// <summary>
/// The user's persisted run-start parameters: the analysis block size (the detector
/// input window, in samples), the capture buffer length (in milliseconds), and the
/// error-rate averaging period (seconds). Unlike the accept bands these are read once
/// at run start rather than applied live, so editing them takes effect on the next run,
/// but they still persist across restarts so a tuned value survives (the project's
/// modifiability/usability driver).
///
/// All values are constrained to an editable range AND a step grid (block size a
/// multiple of <see cref="BlockSizeStepSamples"/>, buffer a multiple of
/// <see cref="CaptureBufferStepMs"/>, averaging period a multiple of
/// <see cref="AveragingPeriodStepSeconds"/>); the per-field normalizers snap-and-clamp
/// any candidate, so an off-step or out-of-range value can never reach the pipeline or
/// the persisted file.
///
/// Thread confinement: <see cref="Current"/> is published on startup (before the UI
/// thread runs) as the seed for the Settings inputs and is kept in sync by the
/// controller on each accepted edit; it is read on the UI thread (seed + run start),
/// and the values reach Core only by value, so the analysis worker and Core never
/// read this record.
/// </summary>
internal sealed record SamplingSettings(
    int AnalysisBlockSize,
    int CaptureBufferMs,
    int AveragingPeriod = 10)
{
    // Editable bounds and step grid — the same limits/increments the SettingsWindow
    // NumericUpDown controls expose. IsValid enforces them so a hand-edited or corrupt
    // JSON file cannot load a value the UI cannot represent or that would degrade the
    // pipeline; the floors/ceilings are themselves multiples of the step.
    public const int BlockSizeFloorSamples = 256;
    public const int BlockSizeCeilingSamples = 16384;
    public const int BlockSizeStepSamples = 256;
    public const int CaptureBufferFloorMs = 5;
    public const int CaptureBufferCeilingMs = 200;
    public const int CaptureBufferStepMs = 5;
    public const int DefaultAveragingPeriodSeconds = 10;
    public const int AveragingPeriodFloorSeconds = 1;
    public const int AveragingPeriodCeilingSeconds = 240;
    public const int AveragingPeriodStepSeconds = 1;

    public static SamplingSettings Default { get; } =
        new(
            AnalysisWorker.DefaultBlockSamples,
            LiveAudioDefaults.BufferMilliseconds,
            DefaultAveragingPeriodSeconds);

    /// <summary>The shared snapshot: seeded on startup and replaced (not mutated) on each accepted edit.</summary>
    public static SamplingSettings Current { get; set; } = Default;

    /// <summary>
    /// True when all values are within the editable bounds AND on their step grid —
    /// the precondition for usable, UI-representable run-start parameters.
    /// Not persisted.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        AnalysisBlockSize >= BlockSizeFloorSamples && AnalysisBlockSize <= BlockSizeCeilingSamples &&
        AnalysisBlockSize % BlockSizeStepSamples == 0 &&
        CaptureBufferMs >= CaptureBufferFloorMs && CaptureBufferMs <= CaptureBufferCeilingMs &&
        CaptureBufferMs % CaptureBufferStepMs == 0 &&
        AveragingPeriod >= AveragingPeriodFloorSeconds && AveragingPeriod <= AveragingPeriodCeilingSeconds &&
        AveragingPeriod % AveragingPeriodStepSeconds == 0;

    /// <summary>
    /// The apply-gate the Settings handler uses: true when <paramref name="candidate"/>
    /// is valid and actually differs from this one, so an out-of-range or no-op edit
    /// neither persists nor re-applies. Pure, so it is unit-testable without the window.
    /// </summary>
    public bool ShouldReplace(SamplingSettings candidate) =>
        candidate.IsValid && candidate != this;

    /// <summary>Clamps to the editable range and snaps to the nearest step multiple, so any
    /// raw input becomes an in-range, step-aligned, usable block size.</summary>
    public static int NormalizeAnalysisBlockSize(int value) =>
        SnapClamp(value, BlockSizeFloorSamples, BlockSizeCeilingSamples, BlockSizeStepSamples);

    /// <summary>Clamps to the editable range and snaps to the nearest step multiple (ms).</summary>
    public static int NormalizeCaptureBufferMs(int value) =>
        SnapClamp(value, CaptureBufferFloorMs, CaptureBufferCeilingMs, CaptureBufferStepMs);

    /// <summary>Clamps to the editable range and snaps to the nearest step multiple (seconds).</summary>
    public static int NormalizeAveragingPeriod(int value) =>
        SnapClamp(value, AveragingPeriodFloorSeconds, AveragingPeriodCeilingSeconds, AveragingPeriodStepSeconds);

    // Decimal overloads for the NumericUpDown.Value (decimal?) binding path: round to the
    // nearest whole unit, then snap-and-clamp.
    public static int NormalizeAnalysisBlockSize(decimal value) =>
        NormalizeAnalysisBlockSize((int)Math.Round(value, MidpointRounding.AwayFromZero));

    public static int NormalizeCaptureBufferMs(decimal value) =>
        NormalizeCaptureBufferMs((int)Math.Round(value, MidpointRounding.AwayFromZero));

    public static int NormalizeAveragingPeriod(decimal value) =>
        NormalizeAveragingPeriod((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static int SnapClamp(int value, int floor, int ceiling, int step)
    {
        int clamped = Math.Clamp(value, floor, ceiling);
        int snapped = (int)Math.Round((double)clamped / step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(snapped, floor, ceiling);
    }
}
