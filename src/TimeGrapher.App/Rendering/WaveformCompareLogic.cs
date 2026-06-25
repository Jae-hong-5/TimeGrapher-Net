using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure logic behind the Waveform Comparison tab, kept out of the renderer so
/// it is unit-testable without a live plot control: the error rate / beat error /
/// BPH header line, the per-lane phase + A→C label, the mean C-peak guide
/// position (the cross-beat consistency reference) and the review-cursor
/// mapping onto the A-aligned lane axis.
/// </summary>
internal static class WaveformCompareLogic
{
    /// <summary>Stacked lanes (one per ring entry the capture publishes).</summary>
    public const int MaxLanes = BeatSegmentCapture.SegmentRingCount;

    /// <summary>
    /// Number of tic/toc pair lanes rendered (each lane shows one tic and one
    /// toc segment side-by-side: tic on the left half, toc on the right half).
    /// </summary>
    public const int PairLanes = MaxLanes / 2;

    /// <summary>
    /// Display width per beat side (half of the full capture window). Each of
    /// the tic and toc halves shows this many milliseconds after the A event,
    /// which is enough to capture the C peak while keeping the two beats
    /// visually separate within a pair lane.
    /// </summary>
    public const double BeatDisplayWindowMs = BeatSegmentCapture.WindowMs / 2;

    /// <summary>
    /// X offset applied to the toc waveform so it appears immediately to the
    /// right of the tic half within the same lane.
    /// </summary>
    public const double TocXOffsetMs = BeatDisplayWindowMs;

    /// <summary>
    /// Vertical offset between lane baselines. Each lane normalizes to its own
    /// peak (height 1.0), so 1.2 leaves a 0.2 gap between lanes.
    /// </summary>
    public const double LaneSpacing = 1.2;

    private const string SignedTenthsMsFormat = "+0.0;-0.0;0.0";

    /// <summary>The A guide sits at x = 0 — every lane is aligned on its A onset.</summary>
    public const string AGuideLabel = "A";

    /// <summary>
    /// Header line: the plan's key numerics (error rate, beat error, BPH)
    /// read from the cumulative metrics-history currents (em dash when absent).
    /// </summary>
    public static string HeaderLine(BeatMetricsHistorySnapshot? history) =>
        "Instantaneous Rate " + VarioReadout.Format(
            history is { RateValid: true } ? history.RateSPerDay : null, "+0.0;-0.0;0.0", " s/d")
        + "   |   Instantaneous Beat Err " + VarioReadout.Format(
            history is { BeatErrorValid: true } ? history.BeatErrorSignedMs : null, "+0.00;-0.00;0.00", " ms")
        + "   |   BPH " + VarioReadout.Format(
            history is { Bph: > 0 } ? history.Bph : null, "0", "");

    /// <summary>
    /// Per-lane label: the alternating phase the segment was captured on plus
    /// that beat's own A→C peak interval (em dash while the C is missing), and
    /// the calculated balance-wheel amplitude. The amplitude uses the canonical
    /// escapement formula <see cref="WatchMetrics.Amplitude"/> shared with every
    /// other readout, so the lane label matches the Vario/Trace/sequence value
    /// for the same beat.
    /// </summary>
    public static string LaneLabel(BeatSegment segment, int bph, double liftAngleDeg) =>
        (segment.IsTic ? "TIC" : "TOC") + "\n" +
        "A to C: " + (segment.CPeakValid
            ? (segment.CPeakOffsetMs - segment.AOffsetMs)
                .ToString(SignedTenthsMsFormat, CultureInfo.InvariantCulture) + " ms"
            : VarioReadout.Missing) + "\n" +
        "Amplitude: " + (segment.Samples.Length > 0 && segment.CPeakValid && bph > 0
            && CalculateAmplitude(segment, bph, liftAngleDeg) is double amplitudeDeg
            ? amplitudeDeg.ToString("F1", CultureInfo.InvariantCulture) + "°"
            : VarioReadout.Missing);

    /// <summary>
    /// Balance-wheel amplitude (degrees) from the A→C peak interval, via the
    /// canonical escapement formula <see cref="WatchMetrics.Amplitude"/>
    /// (lift angle / sin) — the single amplitude source of truth across the app,
    /// rather than a small-angle linear approximation that would diverge from the
    /// Vario/Trace/sequence readouts at lower amplitudes. Returns null for an
    /// out-of-range result (>= 360°, or the non-finite value a C near the
    /// half-oscillation period produces), applying the same validity cap as
    /// <see cref="WatchMetrics"/> so the lane shows '—' exactly where the other
    /// readouts do.
    /// </summary>
    private static double? CalculateAmplitude(BeatSegment segment, int bph, double liftAngleDeg)
    {
        double tACSeconds = (segment.CPeakOffsetMs - segment.AOffsetMs) / 1000.0;
        if (tACSeconds <= 0.0)
        {
            return null;
        }

        double amplitude = WatchMetrics.Amplitude(liftAngleDeg, tACSeconds, bph);
        // Canonical validity window (WatchMetrics.ComputeAmplitude): a positive
        // angle below 360 deg. ">0 && <360" also rejects +inf and NaN, and a
        // negative amplitude from a C past the half-cycle (Sin() < 0).
        return amplitude > 0.0 && amplitude < 360.0 ? amplitude : (double?)null;
    }

    /// <summary>
    /// Assigns the two segments of a comparison lane to their tic (left) / toc
    /// (right) halves by each segment's own phase. Consecutive captures normally
    /// alternate, but a skipped beat can make a pair the same phase; in that case
    /// the half for the missing phase is null (drawn empty) and the newer segment
    /// keeps its real half, so a beat is never drawn in the wrong half or
    /// mislabeled — and the review cursor, which maps by real phase, stays
    /// consistent. <paramref name="older"/> precedes <paramref name="newer"/>.
    /// </summary>
    public static (BeatSegment? Tic, BeatSegment? Toc) AssignPairHalves(BeatSegment older, BeatSegment newer)
    {
        BeatSegment? tic = newer.IsTic ? newer : (older.IsTic ? older : (BeatSegment?)null);
        BeatSegment? toc = !newer.IsTic ? newer : (!older.IsTic ? older : (BeatSegment?)null);
        return (tic, toc);
    }

    /// <summary>
    /// The segments actually drawn for the comparison lanes, using the same
    /// newest-first pairing the renderer uses (<see cref="AssignPairHalves"/> per
    /// pair). A same-phase pair contributes only its newer segment, so the older
    /// duplicate is excluded. Returned oldest-first so the mean-C guides and the
    /// review cursor see exactly the beats on screen — never a hidden one.
    /// </summary>
    public static IReadOnlyList<BeatSegment> VisibleSegments(IReadOnlyList<BeatSegment> segments)
    {
        var visible = new List<BeatSegment>(segments.Count);
        for (int lane = 0; lane < PairLanes; lane++)
        {
            int idxLast = segments.Count - 1 - lane * 2;
            int idxFirst = idxLast - 1;
            if (idxFirst < 0)
            {
                continue;
            }

            (BeatSegment? tic, BeatSegment? toc) = AssignPairHalves(segments[idxFirst], segments[idxLast]);
            if (tic is BeatSegment ticSeg)
            {
                visible.Add(ticSeg);
            }

            if (toc is BeatSegment tocSeg)
            {
                visible.Add(tocSeg);
            }
        }

        visible.Sort((a, b) => a.StartTimeS.CompareTo(b.StartTimeS));
        return visible;
    }

    /// <summary>
    /// Mean A→C peak interval (ms) across the shown lanes — the cross-beat
    /// consistency reference the guide marker draws. Null until any lane
    /// carries a valid C peak. When <paramref name="ticOnly"/> is not null,
    /// only segments whose <see cref="BeatSegment.IsTic"/> matches are included.
    /// </summary>
    public static double? MeanCPeakOffsetMs(IReadOnlyList<BeatSegment> segments,
        bool? ticOnly = null)
    {
        double sum = 0.0;
        int count = 0;
        foreach (BeatSegment segment in segments)
        {
            if (ticOnly.HasValue && segment.IsTic != ticOnly.Value)
            {
                continue;
            }

            if (segment.CPeakValid && (segment.Quality & SignalQualityFlags.PossibleFalseC) == 0)
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
    /// Lane (pair) index for a data-space Y coordinate, or -1 when outside
    /// all rendered lanes. Lane 0 is topmost/newest, lane
    /// <paramref name="pairCount"/>-1 is oldest.
    /// </summary>
    public static int PairFromDataY(double dataY, int pairCount)
    {
        if (pairCount <= 0)
        {
            return -1;
        }

        int lane = PairLanes - 1 - (int)Math.Floor(dataY / LaneSpacing);
        return lane >= 0 && lane < pairCount ? lane : -1;
    }

    /// <summary>
    /// Review-cursor contract on the lane plot: the x-domain is milliseconds
    /// relative to each lane's A onset, so the scrubbed stream time maps to its
    /// A-relative offset in the newest lane whose window contains it (windows
    /// overlap; the newest is the lane a viewer reads first). A toc segment is
    /// rendered shifted into the right half, so its cursor offset is shifted by
    /// <paramref name="tocXOffsetMs"/> to land on the toc half. Null hides the
    /// cursor (live, no lanes, or a time outside every window).
    /// </summary>
    public static double? CursorOffsetMs(double? reviewCursorTimeS, IReadOnlyList<BeatSegment> segments,
        double tocXOffsetMs)
    {
        if (reviewCursorTimeS is not double timeS)
        {
            return null;
        }

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            BeatSegment segment = segments[i];
            double offsetMs = (timeS - segment.StartTimeS) * 1000.0;
            // Each half is rendered only out to the clip (tocXOffsetMs), not the
            // full captured window, so accept the cursor only where signal is drawn.
            double renderedWindowMs = tocXOffsetMs + segment.AOffsetMs;
            if (offsetMs >= 0.0 && offsetMs <= renderedWindowMs)
            {
                double aRelativeMs = offsetMs - segment.AOffsetMs;
                return segment.IsTic ? aRelativeMs : tocXOffsetMs + aRelativeMs;
            }
        }

        return null;
    }
}
