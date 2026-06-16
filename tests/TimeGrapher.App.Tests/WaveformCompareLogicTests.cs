using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure logic behind the Waveform Comparison tab: the rate / beat error / BPH
/// header line, per-lane phase + A→C labels, the mean C-peak consistency guide
/// and the review-cursor mapping onto the A-aligned lane axis.
/// </summary>
public sealed class WaveformCompareLogicTests
{
    private static BeatSegment Segment(
        double startTimeS = 0.0, bool isTic = false, double aMs = 5.0, double? cPeakMs = null) => new()
        {
            Samples = new float[1600],
            MsPerPoint = 0.25,
            StartTimeS = startTimeS,
            IsTic = isTic,
            AOffsetMs = aMs,
            CPeakValid = cPeakMs is not null,
            CPeakOffsetMs = cPeakMs ?? 0.0,
        };

    [Fact]
    public void HeaderLine_ReportsTheCurrentRateBeatErrorAndBph()
    {
        var history = new BeatMetricsHistorySnapshot
        {
            RateValid = true,
            RateSPerDay = 1.23,
            BeatErrorValid = true,
            BeatErrorSignedMs = -0.351,
            Bph = 21600,
        };

        Assert.Equal(
            "RATE +1.2 s/d   |   BEAT ERROR -0.35 ms   |   BPH 21600",
            WaveformCompareLogic.HeaderLine(history));
    }

    [Fact]
    public void HeaderLine_FallsBackToEmDashesWhileReadingsAreMissing()
    {
        Assert.Equal(
            "RATE —   |   BEAT ERROR —   |   BPH —",
            WaveformCompareLogic.HeaderLine(null));
        Assert.Equal(
            "RATE —   |   BEAT ERROR —   |   BPH —",
            WaveformCompareLogic.HeaderLine(new BeatMetricsHistorySnapshot()));
    }

    [Fact]
    public void LaneLabel_ReportsThePhaseAndTheBeatsOwnAToCPeakInterval()
    {
        var ticLabel = WaveformCompareLogic.LaneLabel(Segment(isTic: true, aMs: 5.0, cPeakMs: 147.5));
        Assert.Equal("TIC\nA to C: +142.5 ms", ticLabel);

        var tocLabel = WaveformCompareLogic.LaneLabel(Segment(isTic: false, cPeakMs: null));
        Assert.Equal("TOC\nA to C: —", tocLabel);
    }

    [Fact]
    public void MeanCPeakOffset_AveragesOnlyTheLanesWithAValidCPeak()
    {
        var segments = new[]
        {
            Segment(aMs: 5.0, cPeakMs: 145.0),  // A→C 140
            Segment(aMs: 5.0, cPeakMs: null),   // skipped
            Segment(aMs: 5.0, cPeakMs: 155.0),  // A→C 150
        };

        Assert.Equal(145.0, WaveformCompareLogic.MeanCPeakOffsetMs(segments)!.Value, 6);
        Assert.Null(WaveformCompareLogic.MeanCPeakOffsetMs(new[] { Segment(cPeakMs: null) }));
        Assert.Null(WaveformCompareLogic.MeanCPeakOffsetMs(Array.Empty<BeatSegment>()));
    }

    [Fact]
    public void CMeanGuideLabel_ReportsTheMeanInterval()
    {
        Assert.Equal("mean C +142.5 ms", WaveformCompareLogic.CMeanGuideLabel(142.5));
    }

    [Fact]
    public void CursorOffset_MapsStreamTimeOntoTheAAlignedAxis()
    {
        // One 400 ms window starting at 10.0 s with A at +5 ms.
        var segments = new[] { Segment(startTimeS: 10.0) };

        Assert.Equal(45.0, WaveformCompareLogic.CursorOffsetMs(10.05, segments)!.Value, 6);
        Assert.Equal(-5.0, WaveformCompareLogic.CursorOffsetMs(10.0, segments)!.Value, 6);
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(9.9, segments));   // before the window
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(10.5, segments));  // after the window
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(null, segments));  // live
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(10.05, Array.Empty<BeatSegment>()));
    }

    [Fact]
    public void CursorOffset_PrefersTheNewestOverlappingWindow()
    {
        var segments = new[]
        {
            Segment(startTimeS: 10.0),
            Segment(startTimeS: 10.125),
        };

        // 10.2 s lies in both windows; the newest lane wins.
        Assert.Equal(70.0, WaveformCompareLogic.CursorOffsetMs(10.2, segments)!.Value, 6);
    }
}
