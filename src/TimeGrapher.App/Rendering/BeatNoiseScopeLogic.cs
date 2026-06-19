using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure logic behind the Beat Noise tab, kept out of the renderer so it
/// is unit-testable without a live plot control: strip-lane slot math and
/// selection toggling, the Scope 2 progress / average-signal-level readout, the
/// lift-angle label and the review-cursor mapping onto the displayed segment.
/// </summary>
internal static class BeatNoiseScopeLogic
{
    /// <summary>Strip-lane slots (one per ring entry the capture publishes).</summary>
    public const int StripCount = BeatSegmentCapture.SegmentRingCount;

    /// <summary>
    /// Strips fill the lane right-aligned (the newest beat lands in the last
    /// slot): which snapshot segment a slot shows, or -1 for an empty slot.
    /// </summary>
    public static int SegmentIndexForSlot(int slot, int segmentCount)
    {
        int index = slot - (StripCount - segmentCount);
        return index >= 0 && index < segmentCount ? index : -1;
    }

    public static int SlotForSegmentIndex(int segmentIndex, int segmentCount) =>
        StripCount - segmentCount + segmentIndex;

    /// <summary>
    /// Slot hit from the pointer's horizontal fraction across the strip lane data
    /// area, or -1 when the click is outside it. A click on the reserved left axis
    /// yields a negative fraction and must select nothing rather than clamp onto
    /// the oldest slot; a fraction of exactly 1.0 (the right edge) still maps to
    /// the newest slot.
    /// </summary>
    public static int StripSlotFromFraction(double fraction) =>
        fraction < 0.0 || fraction > 1.0
            ? -1
            : Math.Clamp((int)(fraction * StripCount), 0, StripCount - 1);

    public static int StripSampleCount(
        BeatNoiseScopeViewMode viewMode,
        int rangeMs,
        int segmentSampleCount,
        double msPerPoint)
    {
        double windowMs = viewMode == BeatNoiseScopeViewMode.AverageAndStrip
            ? BeatNoiseAverager.LaneWindowMs
            : rangeMs;
        int requested = (int)Math.Ceiling(windowMs / msPerPoint);
        return Math.Min(segmentSampleCount, requested);
    }

    public static double StripFractionFromPixel(double x, double width, double leftPadding)
    {
        double dataWidth = width - leftPadding;
        return dataWidth > 0.0 ? (x - leftPadding) / dataWidth : 0.0;
    }

    /// <summary>
    /// X of strip point <paramref name="p"/> of <paramref name="points"/> within
    /// its slot lane (a 0.03..0.97 inset of the unit-wide slot). A single-point
    /// strip — which the shared sampler can emit — is centered instead of dividing
    /// by (points - 1), which would write a NaN coordinate and corrupt the plot.
    /// </summary>
    public static double StripPointX(int slot, int p, int points) =>
        slot + 0.03 + 0.94 * (points > 1 ? (double)p / (points - 1) : 0.5);

    /// <summary>
    /// Selection toggle: clicking an occupied slot selects it (the main plot
    /// enlarges that slot's beat), clicking it again — or an empty slot —
    /// returns to following the latest beat. Selection is by slot, so it keeps
    /// pointing at the same age relative to the newest beat as the ring
    /// advances (segments themselves rotate out of the pooled buffers and must
    /// not be cached).
    /// </summary>
    public static int? NextSelection(int? currentSlot, int clickedSlot, int segmentCount)
    {
        if (SegmentIndexForSlot(clickedSlot, segmentCount) < 0)
        {
            return null;
        }

        return currentSlot == clickedSlot ? null : clickedSlot;
    }

    /// <summary>The segment the main plot shows: the selected slot's occupant, else the newest.</summary>
    public static BeatSegment? DisplayedSegment(BeatSegmentsSnapshot snapshot, int? selectedSlot)
    {
        if (snapshot.Segments.Count == 0)
        {
            return null;
        }

        if (selectedSlot is int slot)
        {
            int index = SegmentIndexForSlot(slot, snapshot.Segments.Count);
            if (index >= 0)
            {
                return snapshot.Segments[index];
            }
        }

        return snapshot.Segments[^1];
    }

    public static string LiftText(double liftAngleDeg) =>
        "LIFT " + liftAngleDeg.ToString("0.#", CultureInfo.InvariantCulture) + "°";

    /// <summary>
    /// Scope 2 readout: per-lane average signal level (mean of per-interval
    /// envelope peaks) plus the Σ cycle progress. The lanes are presented as
    /// trace 1/2 — never tic/toc — matching the snapshot contract.
    /// </summary>
    public static string AverageLine(BeatNoiseAverageSnapshot average)
    {
        string SignalLevel(int count, double meanPeak) => count > 0
            ? meanPeak.ToString("0.000", CultureInfo.InvariantCulture)
            : "—";

        return "TRACE 1 (top) Signal Level " + SignalLevel(average.Lane1Count, average.Lane1MeanPeak)
            + " · TRACE 2 Signal Level " + SignalLevel(average.Lane2Count, average.Lane2MeanPeak)
            + "   |   " + ProgressText(average);
    }

    /// <summary>Σ cycle progress ("Σ 23/50 · 22/50", "Σ complete", or "Σ off").</summary>
    public static string ProgressText(BeatNoiseAverageSnapshot average)
    {
        if (!average.SigmaEnabled)
        {
            return "Σ off";
        }

        if (average.Frozen)
        {
            return "Σ complete";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Σ {0}/{2} · {1}/{2}",
            average.Lane1Count, average.Lane2Count, average.IntervalsPerLane);
    }

    /// <summary>
    /// Review-cursor contract on the main plot: the x-domain is milliseconds
    /// within the displayed segment's window, so the scrubbed stream time maps
    /// to its in-window offset. Null hides the cursor (live, no segment, or a
    /// time outside the window).
    /// </summary>
    public static double? CursorOffsetMs(double? reviewCursorTimeS, BeatSegment? segment)
    {
        if (reviewCursorTimeS is not double timeS || segment == null)
        {
            return null;
        }

        double offsetMs = (timeS - segment.StartTimeS) * 1000.0;
        double windowMs = segment.MsPerPoint * segment.Samples.Length;
        return offsetMs >= 0.0 && offsetMs <= windowMs ? offsetMs : null;
    }
}

public enum BeatNoiseScopeViewMode
{
    EnvelopeAndStrip,
    AverageAndStrip
}
