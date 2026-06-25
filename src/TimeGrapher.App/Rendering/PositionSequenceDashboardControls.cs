using Avalonia.Controls;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// The Positions tab's live "active position" labels. Cross-position consistency
/// judgment now lives on the Health tab (<see cref="ConsistencyDiagnosis"/>), so
/// the Positions tab is a pure measurement/data view and only tracks which
/// position is currently being recorded.
/// </summary>
internal sealed record PositionSequenceDashboardControls(
    TextBlock ActivePositionText,
    TextBlock ActiveOrientationText);
