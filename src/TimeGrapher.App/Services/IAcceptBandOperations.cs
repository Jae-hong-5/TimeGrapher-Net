namespace TimeGrapher.App.Services;

/// <summary>The five editable accept-band ("normal" range) limits as plain doubles, so the
/// <see cref="AcceptBandController"/> stays free of the Rendering layer's AcceptBandSettings
/// record. Beat error is a single symmetric magnitude.</summary>
internal readonly record struct AcceptBandValues(
    double RateMinSPerDay,
    double RateMaxSPerDay,
    double AmplitudeMinDeg,
    double AmplitudeMaxDeg,
    double BeatErrorMagnitudeMs);

/// <summary>
/// Render/persist side of the accept-band edit flow, driven by <see cref="AcceptBandController"/>
/// — the same view-model-subscriber + operations-interface split as
/// <see cref="IMainWindowSelectionOperations"/>.
/// </summary>
internal interface IAcceptBandOperations
{
    /// <summary>The live limits, read once to seed the Settings inputs on startup.</summary>
    AcceptBandValues CurrentBands { get; }

    /// <summary>Persists and re-applies <paramref name="candidate"/> to every banded graph only when
    /// it is a valid band that differs from the current one (an inverted/no-op edit is ignored).
    /// Returns whether the edit was applied.</summary>
    bool TryApplyEditedBands(AcceptBandValues candidate);
}
