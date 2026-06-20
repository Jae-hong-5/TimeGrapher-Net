using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed record PositionSequenceDashboardControls(
    TextBlock ActivePositionText,
    TextBlock ActiveOrientationText,
    Border ConsistencyBadge,
    TextBlock ConsistencyVerdictText,
    TextBlock ConsistencyDetailText,
    TextBlock SpreadStatusText,
    TextBlock BalanceStatusText,
    TextBlock VerticalHorizontalStatusText,
    TextBlock AverageStatusText,
    TextBlock SpreadRequirementText,
    TextBlock BalanceRequirementText,
    TextBlock VerticalHorizontalRequirementText,
    TextBlock AverageRequirementText,
    TextBlock SpreadReadyText,
    TextBlock BalanceReadyText,
    TextBlock VerticalHorizontalReadyText,
    TextBlock AverageReadyText,
    TextBlock AverageRateText,
    TextBlock AverageAmplitudeText,
    TextBlock SpreadRateText,
    TextBlock SpreadAmplitudeText,
    TextBlock BalanceWheelSpreadText,
    TextBlock VerticalRateText,
    TextBlock HorizontalRateText,
    TextBlock VerticalHorizontalDeltaText);
