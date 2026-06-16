using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed record PositionSequenceDashboardControls(
    TextBlock ActivePositionText,
    TextBlock ActiveOrientationText,
    IReadOnlyList<PositionMapTileControls> PositionMapTiles,
    Border ConsistencyBadge,
    TextBlock ConsistencyVerdictText,
    TextBlock ConsistencyDetailText,
    TextBlock ConsistencyGuideText,
    TextBlock AverageRateText,
    TextBlock AverageAmplitudeText,
    TextBlock SpreadRateText,
    TextBlock SpreadAmplitudeText,
    TextBlock VerticalRateText,
    TextBlock HorizontalRateText,
    TextBlock VerticalHorizontalDeltaText);

internal sealed record PositionMapTileControls(
    WatchPosition Position,
    Border Tile);
