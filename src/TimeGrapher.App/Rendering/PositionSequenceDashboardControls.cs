using Avalonia.Controls;

namespace TimeGrapher.App.Rendering;

internal sealed record PositionSequenceDashboardControls(
    TextBlock LiveRate,
    TextBlock LiveAmplitude,
    TextBlock LiveBeatError,
    TextBlock LiveBeats);
