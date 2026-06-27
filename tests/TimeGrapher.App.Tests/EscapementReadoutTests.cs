using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure formatting behind the Escapement Analyzer marker labels and numeric
/// panel: current A→C readings per reference, the onset-vs-peak delta, the
/// windowed mean±sigma lines and the more-repeatable verdict label.
/// </summary>
public sealed class EscapementReadoutTests
{
    private static BeatSegment Segment(
        double aMs, double? cPeakMs, double? cOnsetMs) => new()
        {
            MsPerPoint = 0.25,
            StartTimeS = 0.0,
            AOffsetMs = aMs,
            CPeakValid = cPeakMs is not null,
            CPeakOffsetMs = cPeakMs ?? 0.0,
            COnsetValid = cOnsetMs is not null,
            COnsetOffsetMs = cOnsetMs ?? 0.0,
        };

    [Fact]
    public void MarkerLabels_ReportTheElapsedMsFromA()
    {
        Assert.Equal("A", EscapementReadout.AMarkerLabel);
        Assert.Equal("C peak +142.50 ms", EscapementReadout.CPeakMarkerLabel(142.5));
        Assert.Equal("C onset +141.80 ms", EscapementReadout.COnsetMarkerLabel(141.8));
    }

    [Fact]
    public void Values_MatchTheLabelOrderForACompleteBeat()
    {
        var tracker = new EscapementTimingTracker();
        BeatSegment latest = Segment(aMs: 5.0, cPeakMs: 147.5, cOnsetMs: 146.75);
        tracker.Accumulate(new BeatSegmentsSnapshot { Version = 1, Segments = new[] { latest } });

        string[] values = EscapementReadout.Values(latest, tracker);

        Assert.Equal(EscapementReadout.Labels.Length, values.Length);
        Assert.Equal("+142.50 ms", values[0]);
        Assert.Equal("+141.75 ms", values[1]);
        Assert.Equal("-0.75 ms", values[2]);
        Assert.Equal("142.50 ±0.00 ms (n=1)", values[3]);
        Assert.Equal("141.75 ±0.00 ms (n=1)", values[4]);
        Assert.Equal("—", values[5]);
    }

    [Fact]
    public void Values_FallBackToEmDashesWhileReadingsAreMissing()
    {
        var tracker = new EscapementTimingTracker();

        // No beat yet at all.
        Assert.All(EscapementReadout.Values(null, tracker), value => Assert.Equal("—", value));

        // A beat whose C never arrived in the window: nothing to measure.
        string[] noC = EscapementReadout.Values(Segment(5.0, cPeakMs: null, cOnsetMs: null), tracker);
        Assert.All(noC, value => Assert.Equal("—", value));

        // C peak without a located onset: the onset readings stay em dashes.
        string[] peakOnly = EscapementReadout.Values(Segment(5.0, cPeakMs: 150.0, cOnsetMs: null), tracker);
        Assert.Equal("+145.00 ms", peakOnly[0]);
        Assert.Equal("—", peakOnly[1]);
        Assert.Equal("—", peakOnly[2]);
    }

    [Fact]
    public void VerdictLabel_CoversOnsetPeakAndUndecided()
    {
        Assert.Equal("ONSET", EscapementReadout.VerdictLabel(EscapementReferenceVerdict.Onset));
        Assert.Equal("PEAK", EscapementReadout.VerdictLabel(EscapementReferenceVerdict.Peak));
        Assert.Equal("—", EscapementReadout.VerdictLabel(EscapementReferenceVerdict.Undecided));
    }
}
