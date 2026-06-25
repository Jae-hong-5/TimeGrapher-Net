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
    bool SimulationRealistic)
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
        SimulationRealistic: true);

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
        IsFiniteBetween(SimulationBeatError, -10.0, 10.0);

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
    bool MeasurementLogEnabled)
{
    public static SettingsWindowSettings Default { get; } = new(
        UseCOnset: false,
        WeakAOnsetRescue: true,
        SpuriousBeatRejection: true,
        PauseOnPositionChange: false,
        HighPassCutoffText: "200",
        MeasurementLogEnabled: false);

    [JsonIgnore]
    public bool IsValid => HighPassCutoffText != null;
}
