using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Tabs;

internal sealed record AnalysisTabResetContext(
    int SampleRate,
    double RateErrorYScale,
    int RateDataPoints,
    WatchPosition ActivePosition = WatchPosition.CH);
