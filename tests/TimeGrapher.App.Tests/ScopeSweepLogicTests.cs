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

        Assert.Equal("ERROR RATE -3.2 s/d   |   Amplitude 282°   |   BEAT ERROR +0.46 ms", line);
    }

    [Fact]
    public void ReferenceLine_ShowsDashesWhileReadingsAreAbsent()
    {
        string empty = ScopeSweepReadout.ReferenceLine(null);
        string invalid = ScopeSweepReadout.ReferenceLine(new BeatMetricsHistorySnapshot());

        foreach (string line in new[] { empty, invalid })
        {
            Assert.Equal("ERROR RATE —   |   Amplitude —   |   BEAT ERROR —", line);
        }
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
}
