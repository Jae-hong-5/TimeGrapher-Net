using TimeGrapher.Core.Metrics;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Locks the title-bar readout formatting: fixed-width fields (so the line never shifts as
/// values change), value-span markers around numbers only, and the constant separators/units.
/// </summary>
public sealed class WatchMetricsResultsTests
{
    private const char S = WatchMetrics.ValueSpanStart; // '{'
    private const char E = WatchMetrics.ValueSpanEnd;   // '}'

    [Fact]
    public void AllInvalid_RendersDashPlaceholdersWithoutMarkers()
    {
        string r = WatchMetrics.BuildResults(false, 0, false, 0.0, false, 0.0, false, 0.0);

        Assert.Equal("ERROR RATE ------ s/d | Amplitude ---° | BEAT ERROR ---- ms | BEAT ----- bph", r);
        // Dashes are placeholders, not numbers, so they carry no accent markers.
        Assert.DoesNotContain(S, r);
        Assert.DoesNotContain(E, r);
    }

    [Fact]
    public void AllValid_WrapsEachNumberInMarkersWithFixedWidths()
    {
        string r = WatchMetrics.BuildResults(true, 21600, true, 1.2, true, 0.3, true, 271);

        Assert.Equal(
            $"ERROR RATE {S}  +1.2{E} s/d | Amplitude {S}271{E}° | BEAT ERROR {S} 0.3{E} ms | BEAT {S}21600{E} bph",
            r);
    }

    [Fact]
    public void NumericFieldsAreFixedWidthRegardlessOfMagnitude()
    {
        string small = WatchMetrics.BuildResults(true, 18000, true, 1.2, true, 0.3, true, 45);
        string large = WatchMetrics.BuildResults(true, 28800, true, -99.9, true, -9.9, true, 320);
        // Rate width 6 ("%+6.1f" in the original) must absorb |rate| >= 100
        // without shifting the line — the Verify fixtures reach +286 s/d.
        string huge = WatchMetrics.BuildResults(true, 21600, true, 286.0, true, 0.0, true, 181);

        Assert.Equal(small.Length, large.Length);
        Assert.Equal(small.Length, huge.Length);
        Assert.Equal(
            $"ERROR RATE {S}+286.0{E} s/d | Amplitude {S}181{E}° | BEAT ERROR {S} 0.0{E} ms | BEAT {S}21600{E} bph",
            huge);
    }

    [Fact]
    public void RateAlwaysCarriesExplicitSign()
    {
        string positive = WatchMetrics.BuildResults(false, 0, true, 5.0, false, 0.0, false, 0.0);
        string negative = WatchMetrics.BuildResults(false, 0, true, -5.0, false, 0.0, false, 0.0);

        Assert.Equal(
            $"ERROR RATE {S}  +5.0{E} s/d | Amplitude ---° | BEAT ERROR ---- ms | BEAT ----- bph",
            positive);
        Assert.Equal(
            $"ERROR RATE {S}  -5.0{E} s/d | Amplitude ---° | BEAT ERROR ---- ms | BEAT ----- bph",
            negative);
    }

    [Fact]
    public void AmplitudeRoundsHalfAwayFromZero()
    {
        string r = WatchMetrics.BuildResults(false, 0, false, 0.0, false, 0.0, true, 270.5);
        Assert.Equal(
            $"ERROR RATE ------ s/d | Amplitude {S}271{E}° | BEAT ERROR ---- ms | BEAT ----- bph",
            r);
    }
}
