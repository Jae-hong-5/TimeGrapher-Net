using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure logic behind the Waveform Comparison tab, kept out of the renderer so
/// it is unit-testable without a live plot control: the rate / beat error /
/// BPH header line, the per-lane phase + A→C label, the mean C-peak guide
/// position (the cross-beat consistency reference) and the review-cursor
/// mapping onto the A-aligned lane axis.
/// </summary>
internal static class WaveformCompareLogic
{
    /// <summary>Stacked lanes (one per ring entry the capture publishes).</summary>
    public const int MaxLanes = BeatSegmentCapture.SegmentRingCount;

    /// <summary>
    /// Vertical offset between lane baselines. Each lane normalizes to its own
    /// peak (height 1.0), so 1.2 leaves a 0.2 gap between lanes.
    /// </summary>
    public const double LaneSpacing = 1.2;

    private const string SignedTenthsMsFormat = "+0.0;-0.0;0.0";

    /// <summary>The A guide sits at x = 0 — every lane is aligned on its A onset.</summary>
    public const string AGuideLabel = "A";

    /// <summary>
    /// Header line: the plan's key numerics (rate, beat error, beats per hour)
    /// read from the cumulative metrics-history currents (em dash when absent).
    /// </summary>
    public static string HeaderLine(BeatMetricsHistorySnapshot? history) =>
        "RATE " + VarioReadout.Format(
            history is { RateValid: true } ? history.RateSPerDay : null, "+0.0;-0.0;0.0", " s/d")
        + "   |   BEAT ERROR " + VarioReadout.Format(
            history is { BeatErrorValid: true } ? history.BeatErrorSignedMs : null, "+0.00;-0.00;0.00", " ms")
        + "   |   BPH " + VarioReadout.Format(
            history is { Bph: > 0 } ? history.Bph : null, "0", "");

    /// <summary>
    /// Per-lane label: the alternating phase the segment was captured on plus
    /// that beat's own A→C peak interval (em dash while the C is missing), so
    /// each lane's spacing reads against the mean C guide.
    /// </summary>
    public static string LaneLabel(BeatSegment segment) =>
        (segment.IsTic ? "TIC" : "TOC") + " · C " + (segment.CPeakValid
            ? (segment.CPeakOffsetMs - segment.AOffsetMs)
                .ToString(SignedTenthsMsFormat, CultureInfo.InvariantCulture) + " ms"
            : VarioReadout.Missing);

    /// <summary>
    /// Mean A→C peak interval (ms) across the shown lanes — the cross-beat
    /// consistency reference the guide marker draws. Null until any lane
    /// carries a valid C peak.
    /// </summary>
    public static double? MeanCPeakOffsetMs(IReadOnlyList<BeatSegment> segments)
    {
        double sum = 0.0;
        int count = 0;
        foreach (BeatSegment segment in segments)
        {
            if (segment.CPeakValid)
            {
                sum += segment.CPeakOffsetMs - segment.AOffsetMs;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }

    public static string CMeanGuideLabel(double meanCPeakOffsetMs) =>
        "mean C " + meanCPeakOffsetMs.ToString(SignedTenthsMsFormat, CultureInfo.InvariantCulture) + " ms";

    /// <summary>
    /// Review-cursor contract on the lane plot: the x-domain is milliseconds
    /// relative to each lane's A onset, so the scrubbed stream time maps to its
    /// A-relative offset in the newest lane whose window contains it (windows
    /// overlap; the newest is the lane a viewer reads first). Null hides the
    /// cursor (live, no lanes, or a time outside every window).
    /// </summary>
    public static double? CursorOffsetMs(double? reviewCursorTimeS, IReadOnlyList<BeatSegment> segments)
    {
        if (reviewCursorTimeS is not double timeS)
        {
            return null;
        }

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            BeatSegment segment = segments[i];
            double offsetMs = (timeS - segment.StartTimeS) * 1000.0;
            double windowMs = segment.MsPerPoint * segment.Samples.Length;
            if (offsetMs >= 0.0 && offsetMs <= windowMs)
            {
                return offsetMs - segment.AOffsetMs;
            }
        }

        return null;
    }
}
