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
        double startTimeS = 0.0, bool isTic = false, double aMs = 5.0, double? cPeakMs = null,
        float samplePeak = 0.0f)
    {
        var samples = new float[1600];
        if (samplePeak != 0.0f)
        {
            samples[0] = samplePeak;
        }

        return new()
        {
            Samples = samples,
            MsPerPoint = 0.25,
            StartTimeS = startTimeS,
            IsTic = isTic,
            AOffsetMs = aMs,
            CPeakValid = cPeakMs is not null,
            CPeakOffsetMs = cPeakMs ?? 0.0,
        };
    }

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
        int bph = 18000;  // Test with 18000 BPH
        var ticLabel = WaveformCompareLogic.LaneLabel(Segment(isTic: true, aMs: 5.0, cPeakMs: 147.5), bph, liftAngleDeg: 52.0);
        Assert.Equal("TIC\nA to C: +142.5 ms\nAmp: 66.2°", ticLabel);

        var tocLabel = WaveformCompareLogic.LaneLabel(Segment(isTic: false, cPeakMs: null), bph, liftAngleDeg: 52.0);
        Assert.Equal("TOC\nA to C: —\nAmp: —", tocLabel);
    }

    [Fact]
    public void LaneLabel_AmplitudeUsesTheConfiguredLiftAngleNotTheSampleMagnitude()
    {
        // Amp uses the canonical escapement formula WatchMetrics.Amplitude
        // (lift / sin), shared with every other readout. λ=52°, n=18000 bph,
        // t_AC = 147.5-5.0 = 142.5 ms => 52 / sin(2π·0.1425/0.4) ≈ 66.2°.
        Assert.Equal(
            "TIC\nA to C: +142.5 ms\nAmp: 66.2°",
            WaveformCompareLogic.LaneLabel(
                Segment(isTic: true, aMs: 5.0, cPeakMs: 147.5, samplePeak: 0.1f), bph: 18000, liftAngleDeg: 52.0));

        // The lift angle is a configured (left-panel) value, not derived from the
        // captured envelope, so a louder capture must not change the amplitude.
        Assert.Equal(
            "TIC\nA to C: +142.5 ms\nAmp: 66.2°",
            WaveformCompareLogic.LaneLabel(
                Segment(isTic: true, aMs: 5.0, cPeakMs: 147.5, samplePeak: 0.9f), bph: 18000, liftAngleDeg: 52.0));

        // A different configured lift angle scales the amplitude proportionally
        // (60/52 * 66.2 ≈ 76.4°), proving the configured value reaches the formula.
        Assert.Equal(
            "TIC\nA to C: +142.5 ms\nAmp: 76.4°",
            WaveformCompareLogic.LaneLabel(
                Segment(isTic: true, aMs: 5.0, cPeakMs: 147.5, samplePeak: 0.1f), bph: 18000, liftAngleDeg: 60.0));
    }

    [Fact]
    public void LaneLabel_AmplitudeIsMissingWhenOutOfRange()
    {
        // A C peak near the half-oscillation period drives the canonical amplitude
        // toward its sin zero-crossing (>= 360 deg / non-finite), which the canonical
        // readout suppresses as missing. The lane label matches by showing the dash
        // rather than a garbage number. bph=28800 -> half period 125 ms; t_AC=125 ms.
        Assert.Equal(
            "TIC\nA to C: +125.0 ms\nAmp: —",
            WaveformCompareLogic.LaneLabel(
                Segment(isTic: true, aMs: 5.0, cPeakMs: 130.0, samplePeak: 0.1f), bph: 28800, liftAngleDeg: 52.0));
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
        // One 400 ms tic window starting at 10.0 s with A at +5 ms; a tic stays on
        // the left half, so no toc x-offset is applied.
        var segments = new[] { Segment(startTimeS: 10.0, isTic: true) };

        Assert.Equal(45.0, WaveformCompareLogic.CursorOffsetMs(10.05, segments, 200.0)!.Value, 6);
        Assert.Equal(-5.0, WaveformCompareLogic.CursorOffsetMs(10.0, segments, 200.0)!.Value, 6);
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(9.9, segments, 200.0));   // before the window
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(10.5, segments, 200.0));  // after the window
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(null, segments, 200.0));  // live
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(10.05, Array.Empty<BeatSegment>(), 200.0));
    }

    [Fact]
    public void CursorOffset_TocSegmentShiftsIntoTheRightHalf()
    {
        // A toc segment renders shifted right by the toc x-offset, so its cursor
        // offset is tocXOffsetMs + the A-relative offset (200 + 45).
        var segments = new[] { Segment(startTimeS: 10.0, isTic: false) };

        Assert.Equal(245.0, WaveformCompareLogic.CursorOffsetMs(10.05, segments, 200.0)!.Value, 6);
    }

    [Fact]
    public void CursorOffset_HidesWhenScrubbedBeyondTheRenderedClip()
    {
        // Each half is rendered only out to the clip (tocXOffsetMs = 200 ms) even
        // though the captured window is wider, so a scrub past the clip has no drawn
        // signal and the cursor hides instead of pointing at a blank area.
        var segments = new[] { Segment(startTimeS: 10.0, isTic: true) };

        // offset 230 ms (A+225) is past the 200 ms clip (rendered bound 205 ms).
        Assert.Null(WaveformCompareLogic.CursorOffsetMs(10.23, segments, 200.0));
        // ...but a scrub within the clip still shows the cursor.
        Assert.Equal(45.0, WaveformCompareLogic.CursorOffsetMs(10.05, segments, 200.0)!.Value, 6);
    }

    [Fact]
    public void CursorOffset_PrefersTheNewestOverlappingWindow()
    {
        var segments = new[]
        {
            Segment(startTimeS: 10.0, isTic: true),
            Segment(startTimeS: 10.125, isTic: true),
        };

        // 10.2 s lies in both windows; the newest lane wins.
        Assert.Equal(70.0, WaveformCompareLogic.CursorOffsetMs(10.2, segments, 200.0)!.Value, 6);
    }
}
