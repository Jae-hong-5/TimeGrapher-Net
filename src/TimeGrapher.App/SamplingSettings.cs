using System.Text.Json.Serialization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App;

/// <summary>
/// The user's persisted sampling parameters: the analysis block size (the detector
/// input window, in samples) and the capture buffer length (in milliseconds). Unlike
/// the accept bands these are read once at run start rather than applied live — both
/// are construction-time inputs to the analysis/capture pipeline — so editing them
/// takes effect on the next run, but they still persist across restarts so a tuned
/// value survives (the project's modifiability/usability driver).
///
/// Thread confinement: <see cref="Current"/> is published once on startup (before the
/// UI thread runs) as the seed for the Settings inputs; thereafter the live edited
/// values live in the view-model and are read on the UI thread at run start, so the
/// analysis worker and Core never read this record.
/// </summary>
internal sealed record SamplingSettings(
    int AnalysisBlockSize,
    int CaptureBufferMs)
{
    // Editable bounds — the same limits the SettingsWindow NumericUpDown controls expose.
    // IsValid enforces them so a hand-edited or corrupt JSON file cannot load a value the
    // UI cannot represent or that would degrade the pipeline.
    public const int BlockSizeFloorSamples = 256;
    public const int BlockSizeCeilingSamples = 16384;
    public const int CaptureBufferFloorMs = 5;
    public const int CaptureBufferCeilingMs = 200;

    public static SamplingSettings Default { get; } =
        new(AnalysisWorker.DefaultBlockSamples, LiveAudioDefaults.BufferMilliseconds);

    /// <summary>The startup seed for the Settings inputs; replaced (not mutated) once at startup.</summary>
    public static SamplingSettings Current { get; set; } = Default;

    /// <summary>
    /// True when both values are within the editable bounds — the precondition for a
    /// usable block size and capture buffer. Not persisted.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        AnalysisBlockSize >= BlockSizeFloorSamples && AnalysisBlockSize <= BlockSizeCeilingSamples &&
        CaptureBufferMs >= CaptureBufferFloorMs && CaptureBufferMs <= CaptureBufferCeilingMs;

    /// <summary>
    /// The apply-gate the Settings handler uses: true when <paramref name="candidate"/>
    /// is valid and actually differs from this one, so an out-of-range or no-op edit
    /// neither persists nor re-applies. Pure, so it is unit-testable without the window.
    /// </summary>
    public bool ShouldReplace(SamplingSettings candidate) =>
        candidate.IsValid && candidate != this;
}
