using TimeGrapher.App.Rendering;
using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

/// <summary>
/// Render/persist side of the accept-band edit flow (<see cref="IAcceptBandOperations"/>),
/// driven by <see cref="AcceptBandController"/>. Reads the live limits for seeding, and applies
/// an edited band by rebuilding the shared <see cref="AcceptBandSettings"/>, gating on a valid
/// changed band, persisting it, and fanning it out to every banded graph (no run reset, so the
/// history is kept). Lifting it out of the MainWindow code-behind keeps this policy off the View
/// while it still wraps the view-side renderer (it depends on <see cref="GraphFrameRenderer"/>,
/// not on the window, so it adds no view back-edge).
/// </summary>
internal sealed class GraphAcceptBandOperations : IAcceptBandOperations
{
    private readonly GraphFrameRenderer _renderer;
    private readonly Action<AcceptBandSettings> _persist;

    public GraphAcceptBandOperations(GraphFrameRenderer renderer, Action<AcceptBandSettings>? persist = null)
    {
        _renderer = renderer;
        // Persistence is injectable so the accepted path is testable without writing the real
        // user-config file; production uses the static store.
        _persist = persist ?? AcceptBandSettingsStore.Save;
    }

    public AcceptBandValues CurrentBands
    {
        get
        {
            AcceptBandSettings bands = AcceptBandSettings.Current;
            return new AcceptBandValues(
                RateMinSPerDay: bands.RateMinSPerDay,
                RateMaxSPerDay: bands.RateMaxSPerDay,
                AmplitudeMinDeg: bands.AmplitudeMinDeg,
                AmplitudeMaxDeg: bands.AmplitudeMaxDeg,
                BeatErrorMagnitudeMs: bands.BeatErrorMagnitudeMs);
        }
    }

    public bool TryApplyEditedBands(AcceptBandValues candidate)
    {
        var bands = new AcceptBandSettings(
            RateMinSPerDay: candidate.RateMinSPerDay,
            RateMaxSPerDay: candidate.RateMaxSPerDay,
            AmplitudeMinDeg: candidate.AmplitudeMinDeg,
            AmplitudeMaxDeg: candidate.AmplitudeMaxDeg,
            BeatErrorMagnitudeMs: candidate.BeatErrorMagnitudeMs);

        if (!AcceptBandSettings.Current.ShouldReplace(bands))
        {
            return false;
        }

        AcceptBandSettings.Current = bands;
        _persist(bands);
        _renderer.ApplyAcceptBands();
        return true;
    }
}
