using System.Text.Json.Serialization;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App;

internal sealed record AppSettings(
    SamplingSettings Sampling,
    AcceptBandSettings AcceptBands,
    LeftPanelSettings LeftPanel,
    SettingsWindowSettings SettingsWindow)
{
    public static AppSettings Default { get; } = new(
        SamplingSettings.Default,
        AcceptBandSettings.Default,
        LeftPanelSettings.Default,
        SettingsWindowSettings.Default);

    public static AppSettings Current { get; set; } = Default;

    [JsonIgnore]
    public bool IsValid =>
        Sampling is { IsValid: true } &&
        AcceptBands is { IsValid: true } &&
        LeftPanel is { IsValid: true } &&
        SettingsWindow is { IsValid: true };
}

internal sealed record LeftPanelSettings(
    string? InputDeviceName,
    int SampleRate,
    double Gain,
    int Bph,
    double LiftAngle,
    int SimulationBph,
    double SimulationErrorRate,
    double SimulationAmplitude,
    double SimulationBeatError,
    bool SimulationRealistic,
    // Per-cluster A/B/C signal-size scales. Optional (defaulted to 1.0) so a settings
    // file written before these knobs existed still loads, defaulting each cluster to
    // its nominal size rather than silently muting it.
    double SimulationSignalAScale = 1.0,
    double SimulationSignalBScale = 1.0,
    double SimulationSignalCScale = 1.0)
{
    public static LeftPanelSettings Default { get; } = new(
        InputDeviceName: null,
        SampleRate: 48000,
        Gain: 100.0,
        Bph: 0,
        LiftAngle: 52.0,
        SimulationBph: 28800,
        SimulationErrorRate: 0.0,
        SimulationAmplitude: 300.0,
        SimulationBeatError: 0.0,
        SimulationRealistic: true,
        SimulationSignalAScale: 1.0,
        SimulationSignalBScale: 1.0,
        SimulationSignalCScale: 1.0);

    [JsonIgnore]
    public bool IsValid =>
        (InputDeviceName is null || InputDeviceName.Length > 0) &&
        AudioSampleRates.StandardSet.Contains(SampleRate) &&
        IsFiniteBetween(Gain, 0.0, 1000.0) &&
        Contains(BphCatalog.ManualAutoBph, Bph) &&
        IsFiniteBetween(LiftAngle, 30.0, 70.0) &&
        Contains(BphCatalog.ManualBph, SimulationBph) &&
        IsFiniteBetween(SimulationErrorRate, -999.0, 999.0) &&
        IsFiniteBetween(SimulationAmplitude, 100.0, 360.0) &&
        IsFiniteBetween(SimulationBeatError, -10.0, 10.0) &&
        IsFiniteBetween(SimulationSignalAScale, 0.0, 2.0) &&
        IsFiniteBetween(SimulationSignalBScale, 0.0, 2.0) &&
        IsFiniteBetween(SimulationSignalCScale, 0.0, 2.0);

    private static bool IsFiniteBetween(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static bool Contains(IReadOnlyList<int> values, int value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record SettingsWindowSettings(
    bool UseCOnset,
    bool WeakAOnsetRescue,
    bool SpuriousBeatRejection,
    bool PauseOnPositionChange,
    string HighPassCutoffText,
    bool MeasurementLogEnabled,
    int WeakAOnsetRescueStrengthStep = WeakAOnsetRescueStrengthPolicy.MinStep,
    int VerdictMinimumBeats = VerdictBeatPolicy.DefaultMinimumBeats)
{
    public static SettingsWindowSettings Default { get; } = new(
        UseCOnset: false,
        WeakAOnsetRescue: true,
        SpuriousBeatRejection: true,
        PauseOnPositionChange: false,
        HighPassCutoffText: "200",
        MeasurementLogEnabled: true,
        WeakAOnsetRescueStrengthStep: WeakAOnsetRescueStrengthPolicy.MinStep,
        VerdictMinimumBeats: VerdictBeatPolicy.DefaultMinimumBeats);

    [JsonIgnore]
    public bool IsValid =>
        HighPassCutoffText != null &&
        WeakAOnsetRescueStrengthPolicy.IsValidStep(WeakAOnsetRescueStrengthStep) &&
        VerdictBeatPolicy.IsValidMinimumBeats(VerdictMinimumBeats);
}
