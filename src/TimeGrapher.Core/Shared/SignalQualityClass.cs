namespace TimeGrapher.Core.Shared;

/// <summary>
/// Coarse signal-quality verdict for the current measurement window.
///
/// This is an <b>advisory</b> classification: it never drops or alters detected
/// beats, it only annotates how much to trust the live readings and what the
/// user can do about poor input. Contrast with the removed event veto, which
/// dropped beats (an "ignore faulty input" tactic) and could hide the very
/// irregularities a timegrapher exists to surface. Here we apply "condition
/// monitoring": observe and report, never destroy.
///
/// Lives in Core.Shared (alongside SignalQualityFlags) because it is a pure DTO
/// that rides the AnalysisFrame; the classifier behaviour that produces it lives
/// in Core.Analysis.Quality.
/// </summary>
public enum SignalQualityClass
{
    /// <summary>Quality could not be assessed yet (pre-sync / warming up).</summary>
    Unknown = 0,

    /// <summary>Clean lock; readings are trustworthy.</summary>
    Good = 1,

    /// <summary>Strong background noise relative to the tick; readings usable but degraded.</summary>
    Noisy = 2,

    /// <summary>Tick energy is close to the noise floor; reposition the microphone / watch.</summary>
    WeakSignal = 3,

    /// <summary>Timing is erratic (missed beats / sync loss / high jitter); low confidence.</summary>
    Unstable = 4,
}
