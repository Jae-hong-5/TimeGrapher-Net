using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed record PositionSequenceDashboardControls(
    TextBlock ActivePositionText,
    TextBlock ActiveOrientationText,
    Border ConsistencyBadge,
    TextBlock ConsistencyVerdictText,
    TextBlock ConsistencyDetailText,
    TextBlock SpreadRequirementText,
    TextBlock BalanceRequirementText,
    TextBlock VerticalHorizontalRequirementText,
    TextBlock AverageRateText,
    TextBlock AverageAmplitudeText,
    TextBlock SpreadRateText,
    TextBlock SpreadAmplitudeText,
    TextBlock BalanceWheelSpreadText,
    TextBlock VerticalRateText,
    TextBlock HorizontalRateText,
    TextBlock VerticalHorizontalDeltaText);
