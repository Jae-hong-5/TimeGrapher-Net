using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure logic behind the Beat-Noise Scope tab: right-aligned strip slot math,
/// slot-based selection toggling, the Scope 2 readout (trace 1/2 wording, Σ
/// progress) and the review-cursor mapping onto the displayed segment window.
/// </summary>
public sealed class BeatNoiseScopeLogicTests
{
    private static BeatSegment Segment(double startTimeS) => new()
    {
        Samples = new float[1600],
        MsPerPoint = 0.25,
        StartTimeS = startTimeS,
    };

    private static BeatSegmentsSnapshot Snapshot(int segmentCount)
    {
        var segments = new List<BeatSegment>(segmentCount);
        for (int i = 0; i < segmentCount; i++)
        {
            segments.Add(Segment(i * 0.125));
        }

        return new BeatSegmentsSnapshot { Version = 1, Segments = segments };
    }

    [Theory]
    [InlineData(7, 3, 2)]  // newest of 3 sits in the last slot
    [InlineData(5, 3, 0)]  // oldest of 3 sits 3 slots from the right
    [InlineData(4, 3, -1)] // slots left of the fill are empty
    [InlineData(0, 8, 0)]  // full ring fills every slot
    public void StripSlots_AreRightAligned(int slot, int segmentCount, int expectedIndex)
    {
        Assert.Equal(expectedIndex, BeatNoiseScopeLogic.SegmentIndexForSlot(slot, segmentCount));
    }

    [Fact]
    public void SlotForSegmentIndex_InvertsSegmentIndexForSlot()
    {
        for (int count = 1; count <= BeatNoiseScopeLogic.StripCount; count++)
        {
            for (int index = 0; index < count; index++)
            {
                int slot = BeatNoiseScopeLogic.SlotForSegmentIndex(index, count);
                Assert.Equal(index, BeatNoiseScopeLogic.SegmentIndexForSlot(slot, count));
            }
        }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.99, 7)]
    [InlineData(0.5, 4)]
    [InlineData(1.0, 7)] // the right edge still maps to the newest slot
    [InlineData(-0.03125, -1)] // a click on the reserved left axis selects nothing
    [InlineData(1.5, -1)] // beyond the right edge selects nothing
    public void StripSlotFromFraction_MapsTheLaneWidthOntoSlots(double fraction, int expectedSlot)
    {
        Assert.Equal(expectedSlot, BeatNoiseScopeLogic.StripSlotFromFraction(fraction));
    }

    [Theory]
    [InlineData(BeatNoiseScopeViewMode.EnvelopeAndStrip, 20, 1600, 0.25, 80)]
    [InlineData(BeatNoiseScopeViewMode.EnvelopeAndStrip, 200, 1600, 0.25, 800)]
    [InlineData(BeatNoiseScopeViewMode.EnvelopeAndStrip, 400, 1600, 0.25, 1600)]
    [InlineData(BeatNoiseScopeViewMode.EnvelopeAndStrip, 400, 1000, 0.25, 1000)]
    [InlineData(BeatNoiseScopeViewMode.AverageAndStrip, 20, 1600, 0.25, 80)]
    [InlineData(BeatNoiseScopeViewMode.AverageAndStrip, 400, 1600, 0.25, 80)]
    public void StripSampleCount_FollowsViewModeAndClampsToSegment(
        BeatNoiseScopeViewMode mode,
        int rangeMs,
        int segmentSampleCount,
        double msPerPoint,
        int expectedCount)
    {
        Assert.Equal(
            expectedCount,
            BeatNoiseScopeLogic.StripSampleCount(mode, rangeMs, segmentSampleCount, msPerPoint));
    }

    [Theory]
    [InlineData(50.0, 850.0, 50.0, 0.0)]
    [InlineData(450.0, 850.0, 50.0, 0.5)]
    [InlineData(850.0, 850.0, 50.0, 1.0)]
    [InlineData(25.0, 850.0, 50.0, -0.03125)]
    public void StripFractionFromPixel_MapsOnlyTheDataArea(double x, double width, double leftPadding, double expected)
    {
        Assert.Equal(expected, BeatNoiseScopeLogic.StripFractionFromPixel(x, width, leftPadding), precision: 6);
    }

    [Theory]
    [InlineData(0, 0, 4, 0.03)]   // first point sits at the slot's left inset
    [InlineData(0, 3, 4, 0.97)]   // last point at the right inset
    [InlineData(2, 1, 3, 2.5)]    // mid strip, offset by the slot index
    public void StripPointX_MapsPointsAcrossTheSlotInset(int slot, int p, int points, double expected)
    {
        Assert.Equal(expected, BeatNoiseScopeLogic.StripPointX(slot, p, points), precision: 6);
    }

    [Fact]
    public void StripPointX_CentersASingleSamplePointInsteadOfDividingByZero()
    {
        // The shared sampler can emit one point; p / (points - 1) would divide by
        // zero and write a NaN x. The lone point is centered in its slot instead.
        double x = BeatNoiseScopeLogic.StripPointX(slot: 3, p: 0, points: 1);
        Assert.True(double.IsFinite(x));
        Assert.Equal(3.0 + 0.03 + 0.94 * 0.5, x, precision: 6);
    }

    [Fact]
    public void Selection_TogglesOnOccupiedSlotsAndClearsOnEmptyOnes()
    {
        // Click an occupied slot: select; click it again: back to live.
        Assert.Equal(6, BeatNoiseScopeLogic.NextSelection(null, 6, segmentCount: 8));
        Assert.Null(BeatNoiseScopeLogic.NextSelection(6, 6, segmentCount: 8));

        // Click a different occupied slot: move the selection.
        Assert.Equal(3, BeatNoiseScopeLogic.NextSelection(6, 3, segmentCount: 8));

        // Click an empty slot (only 2 segments fill slots 6..7): back to live.
        Assert.Null(BeatNoiseScopeLogic.NextSelection(6, 1, segmentCount: 2));
    }

    [Fact]
    public void DisplayedSegment_IsSelectedSlotOccupantElseNewest()
    {
        BeatSegmentsSnapshot snapshot = Snapshot(3); // slots 5..7

        Assert.Same(snapshot.Segments[^1], BeatNoiseScopeLogic.DisplayedSegment(snapshot, null));
        Assert.Same(snapshot.Segments[0], BeatNoiseScopeLogic.DisplayedSegment(snapshot, 5));

        // A stale selection pointing at an empty slot falls back to the newest.
        Assert.Same(snapshot.Segments[^1], BeatNoiseScopeLogic.DisplayedSegment(snapshot, 2));
        Assert.Null(BeatNoiseScopeLogic.DisplayedSegment(Snapshot(0), null));
    }

    [Fact]
    public void LiftText_PresentsTheConfiguredAngle()
    {
        Assert.Equal("LIFT 52°", BeatNoiseScopeLogic.LiftText(52.0));
        Assert.Equal("LIFT 51.5°", BeatNoiseScopeLogic.LiftText(51.5));
    }

    [Fact]
    public void AverageLine_LabelsLanesAsTracesNeverTicToc()
    {
        var average = new BeatNoiseAverageSnapshot
        {
            SigmaEnabled = true,
            IntervalsPerLane = 50,
            Lane1Count = 23,
            Lane2Count = 22,
            Lane1MeanPeak = 0.382,
            Lane2MeanPeak = 0.391,
        };

        string line = BeatNoiseScopeLogic.AverageLine(average);
        Assert.Equal("TRACE 1 (top) Signal Level 0.382 · TRACE 2 Signal Level 0.391   |   Σ 23/50 · 22/50", line);
    }

    [Fact]
    public void ProgressText_CoversOffRunningAndComplete()
    {
        Assert.Equal("Σ off", BeatNoiseScopeLogic.ProgressText(new BeatNoiseAverageSnapshot()));
        Assert.Equal("Σ 10/50 · 9/50", BeatNoiseScopeLogic.ProgressText(new BeatNoiseAverageSnapshot
        {
            SigmaEnabled = true,
            IntervalsPerLane = 50,
            Lane1Count = 10,
            Lane2Count = 9,
        }));
        Assert.Equal("Σ complete", BeatNoiseScopeLogic.ProgressText(new BeatNoiseAverageSnapshot
        {
            SigmaEnabled = true,
            Frozen = true,
            IntervalsPerLane = 50,
            Lane1Count = 50,
            Lane2Count = 50,
        }));
    }

    [Fact]
    public void CursorOffset_MapsStreamTimeIntoTheDisplayedWindow()
    {
        BeatSegment segment = Segment(startTimeS: 10.0); // 400 ms window

        Assert.Equal(50.0, BeatNoiseScopeLogic.CursorOffsetMs(10.05, segment)!.Value, 6);
        Assert.Equal(0.0, BeatNoiseScopeLogic.CursorOffsetMs(10.0, segment)!.Value, 6);
        Assert.Null(BeatNoiseScopeLogic.CursorOffsetMs(9.9, segment));   // before the window
        Assert.Null(BeatNoiseScopeLogic.CursorOffsetMs(10.5, segment));  // after the window
        Assert.Null(BeatNoiseScopeLogic.CursorOffsetMs(null, segment));  // live
        Assert.Null(BeatNoiseScopeLogic.CursorOffsetMs(10.05, null));    // no segment yet
    }
}
