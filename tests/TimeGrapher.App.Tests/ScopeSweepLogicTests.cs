using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure logic behind the Scope Sweep tab: the reference line of current
/// readings, window-length recovery from the published bin centers, and the
/// review-cursor mapping from stream time onto the sweep window phase.
/// </summary>
public sealed class ScopeSweepLogicTests
{
    [Fact]
    public void ReferenceLine_FormatsCurrentReadings()
    {
        var snapshot = new BeatMetricsHistorySnapshot
        {
            RateValid = true,
            RateSPerDay = -3.2,
            AmplitudeValid = true,
            AmplitudeDeg = 281.6,
            BeatErrorValid = true,
            BeatErrorSignedMs = 0.456,
        };

        string line = ScopeSweepReadout.ReferenceLine(snapshot);

        Assert.Equal(
            "Inst. Rate -3.2 s/d   |   Inst. Amp 282°   |   Inst. Beat Err +0.46 ms" +
            "   |   A to C —   |   Nominal BPH —",
            line);
    }

    [Fact]
    public void Values_FormatCurrentReadingsForFooterCells()
    {
        var snapshot = new BeatMetricsHistorySnapshot
        {
            RateValid = true,
            RateSPerDay = -3.2,
            AmplitudeValid = true,
            AmplitudeDeg = 281.6,
            BeatErrorValid = true,
            BeatErrorSignedMs = 0.456,
            Bph = 28800,
        };
        var segments = new BeatSegmentsSnapshot
        {
            Version = 1,
            Segments = new[]
            {
                new BeatSegment { CPeakValid = true, AOffsetMs = 10.0, CPeakOffsetMs = 70.0 },
            },
        };

        string[] values = ScopeSweepReadout.Values(snapshot, segments);

        Assert.Equal(new[] { "-3.2 s/d", "282°", "+0.46 ms", "+60.0 ms", "28800" }, values);
    }

    [Fact]
    public void ReferenceLine_ShowsDashesWhileReadingsAreAbsent()
    {
        string empty = ScopeSweepReadout.ReferenceLine(null);
        string invalid = ScopeSweepReadout.ReferenceLine(new BeatMetricsHistorySnapshot());

        foreach (string line in new[] { empty, invalid })
        {
            Assert.Equal(
                "Inst. Rate —   |   Inst. Amp —   |   Inst. Beat Err —   |   A to C —   |   Nominal BPH —",
                line);
        }
    }

    [Fact]
    public void ReferenceLine_IncludesAtoCAndNominalBphWhenAvailable()
    {
        var snapshot = new BeatMetricsHistorySnapshot
        {
            RateValid = true, RateSPerDay = 0.0,
            AmplitudeValid = true, AmplitudeDeg = 250.0,
            BeatErrorValid = true, BeatErrorSignedMs = 0.0,
            Bph = 28800,
        };
        var segments = new BeatSegmentsSnapshot
        {
            Version = 1,
            Segments = new[]
            {
                new BeatSegment { CPeakValid = true, AOffsetMs = 10.0, CPeakOffsetMs = 70.0 },
            },
        };

        string line = ScopeSweepReadout.ReferenceLine(snapshot, segments);

        // A→C = 60.0 ms, Nominal BPH = 28800 (the rated beat rate from timing history)
        Assert.Contains("A to C +60.0 ms", line);
        Assert.Contains("Nominal BPH 28800", line);
    }

    [Fact]
    public void ReferenceLine_AtoCUsesMostRecentSegmentWithValidCPeak()
    {
        // Both segments have a valid C peak with DIFFERENT A->C, so "most recent"
        // (newest/last) and "first" diverge: the readout must pick the newest (80 ms),
        // not the older 60 ms.
        var segments = new BeatSegmentsSnapshot
        {
            Version = 1,
            Segments = new[]
            {
                new BeatSegment { CPeakValid = true, AOffsetMs = 5.0, CPeakOffsetMs = 65.0 }, // older: 60 ms
                new BeatSegment { CPeakValid = true, AOffsetMs = 5.0, CPeakOffsetMs = 85.0 }, // newest: 80 ms
            },
        };

        string line = ScopeSweepReadout.ReferenceLine(null, segments);

        Assert.Contains("A to C +80.0 ms", line);
    }

    [Fact]
    public void ReferenceLine_AtoCFallsBackToOlderSegmentWhenNewestCPeakInvalid()
    {
        var segments = new BeatSegmentsSnapshot
        {
            Version = 1,
            Segments = new[]
            {
                new BeatSegment { CPeakValid = true,  AOffsetMs = 5.0,  CPeakOffsetMs = 65.0 },
                new BeatSegment { CPeakValid = false, AOffsetMs = 10.0, CPeakOffsetMs = 0.0  },
            },
        };

        string line = ScopeSweepReadout.ReferenceLine(null, segments);

        // Newest segment has no valid C peak; fall back to the older one: 65 - 5 = 60 ms.
        Assert.Contains("A to C +60.0 ms", line);
    }

    [Fact]
    public void WindowMs_RecoversTheWindowFromBinCenters()
    {
        // First center half a bin in, last center half a bin before the end:
        // 250 ms window with 0.0625 ms bins.
        Assert.Equal(250.0, ScopeSweepReadout.WindowMs(new[] { 0.03125, 125.0, 249.96875 }), 6);
        Assert.Equal(0.0, ScopeSweepReadout.WindowMs(Array.Empty<double>()));
    }

    [Theory]
    [InlineData(null, 250.0, null)]
    [InlineData(1.0, 0.0, null)]      // no window yet -> hidden
    [InlineData(0.1, 250.0, 100.0)]   // 100 ms into the first sweep
    [InlineData(1.1, 250.0, 100.0)]   // wraps: 1100 ms mod 250 ms
    public void CursorPhaseMs_MapsStreamTimeOntoTheSweepPhase(
        double? reviewCursorTimeS, double windowMs, double? expectedPhaseMs)
    {
        double? phase = ScopeSweepReadout.CursorPhaseMs(reviewCursorTimeS, windowMs);

        if (expectedPhaseMs is double expected)
        {
            Assert.NotNull(phase);
            Assert.Equal(expected, phase!.Value, 9);
        }
        else
        {
            Assert.Null(phase);
        }
    }

    [Theory]
    [InlineData(0.1, 250.0, 50.0, 150.0)]   // 100 ms + 50 ms offset = 150 ms
    [InlineData(0.1, 250.0, 200.0, 50.0)]   // 100 ms + 200 ms = 300 ms mod 250 ms = 50 ms
    [InlineData(0.0, 250.0, 125.0, 125.0)]  // stream 0 ms + 125 ms offset = 125 ms
    public void CursorPhaseMs_AppliesTicPhaseOffset(
        double reviewCursorTimeS, double windowMs, double ticPhaseOffsetMs, double expectedPhaseMs)
    {
        double? phase = ScopeSweepReadout.CursorPhaseMs(reviewCursorTimeS, windowMs, ticPhaseOffsetMs);
        Assert.NotNull(phase);
        Assert.Equal(expectedPhaseMs, phase!.Value, 9);
    }
}
