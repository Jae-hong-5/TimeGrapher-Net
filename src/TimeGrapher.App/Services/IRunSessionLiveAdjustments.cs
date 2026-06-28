namespace TimeGrapher.App.Services;

/// <summary>
/// The narrow live-run adjustment seam the selection coordinator forwards to: input
/// volume and the live-adjustable simulation knobs. Implemented by
/// <see cref="RunSessionController"/>; it lets the View-side selection-operations adapter
/// reach the running worker without holding the <c>MainWindow</c>.
/// </summary>
internal interface IRunSessionLiveAdjustments
{
    void SetLiveInputVolume(float normalizedVolume);

    void SetLiveSimulationParameters(
        double rateErrorSPerDay,
        double beatErrorMs,
        double watchAmplitudeDegrees,
        double aClusterLevelScale,
        double bClusterLevelScale,
        double cClusterLevelScale);
}
