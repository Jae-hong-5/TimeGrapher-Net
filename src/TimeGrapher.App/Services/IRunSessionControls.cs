using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// The live analysis-worker knobs the <see cref="RunControlController"/> forwards from
/// view-model edits: Scope Sweep window multiple, active watch position, and Beat Noise
/// Scope 2 Σ averaging. Implemented by <see cref="RunSessionController"/>, which also
/// remembers each value so a later run starts with the user's selection.
/// </summary>
internal interface IRunSessionControls
{
    void SetSweepMultiple(int sweepMultiple);

    void SetActivePosition(WatchPosition position);

    void SetSigmaAveraging(bool enabled);
}
