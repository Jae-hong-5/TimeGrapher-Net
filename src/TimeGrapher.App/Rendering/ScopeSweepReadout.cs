using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure logic behind the Scope Sweep tab, kept out of the renderer so it is
/// unit-testable without a live plot control: the per-cell current readings
/// (the plan's "compare the live waveform against the most recent measurements")
/// and the review-cursor mapping from stream time onto the sweep window phase.
/// </summary>
internal static class ScopeSweepReadout
{
    // Unified with the canonical readout terms (BeatErrorReadout / title bar):
    // Error Rate · Amplitude · Beat Error. "A to C" is the sweep-specific A→C
    // interval the other panels do not show.
    public static readonly string[] Labels =
    {
        "Error Rate", "Amplitude", "Beat Error", "A to C",
    };

    /// <summary>
    /// Per-cell current error rate / amplitude / beat error / A→C (em dash when
    /// absent), aligned with <see cref="Labels"/>. <paramref name="segments"/>
    /// supplies the latest A→C interval.
    /// </summary>
    public static string[] Values(BeatMetricsHistorySnapshot? snapshot,
        BeatSegmentsSnapshot? segments = null) => new[]
    {
        VarioReadout.Format(
            snapshot is { RateValid: true } ? snapshot.RateSPerDay : null, "+0.0;-0.0;0.0", " s/d"),
        VarioReadout.Format(
            snapshot is { AmplitudeValid: true } ? snapshot.AmplitudeDeg : null, "0", "°"),
        VarioReadout.Format(
            snapshot is { BeatErrorValid: true } ? snapshot.BeatErrorSignedMs : null, "+0.00;-0.00;0.00", " ms"),
        FormatAtoC(segments),
    };

    /// <summary>A→C interval from the most recent segment that has a valid C peak.</summary>
    internal static string FormatAtoC(BeatSegmentsSnapshot? segments)
    {
        if (segments?.Segments is not { Count: > 0 } segs) return VarioReadout.Missing;
        for (int i = segs.Count - 1; i >= 0; i--)
        {
            BeatSegment seg = segs[i];
            if (seg.CPeakValid)
                return (seg.CPeakOffsetMs - seg.AOffsetMs)
                    .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms";
        }
        return VarioReadout.Missing;
    }

    /// <summary>
    /// Sweep window length (ms) recovered from the published bin centers: the
    /// first center sits half a bin after 0 and the last half a bin before the
    /// window end, so their sum is the window length.
    /// </summary>
    public static double WindowMs(IReadOnlyList<double> binCentersMs) =>
        binCentersMs.Count > 0 ? binCentersMs[^1] + binCentersMs[0] : 0.0;

    /// <summary>
    /// Review-cursor contract on the sweep plot: the x-domain is the phase
    /// within the sweep window, so the scrubbed stream time maps to where that
    /// instant landed in the sweep. Null hides the cursor (live, or no window).
    /// </summary>
    /// <param name="ticPhaseOffsetMs">
    /// Phase offset (ms) from <see cref="GraphSeriesFrame.TicPhaseOffsetMs"/>:
    /// added to stream time before the modulo so the cursor lands on the
    /// tic-aligned bin rather than raw stream time.
    /// </param>
    public static double? CursorPhaseMs(double? reviewCursorTimeS, double windowMs,
        double ticPhaseOffsetMs = 0.0)
    {
        if (reviewCursorTimeS is not double timeS || windowMs <= 0.0)
        {
            return null;
        }

        double phase = (timeS * 1000.0 + ticPhaseOffsetMs) % windowMs;
        return phase < 0.0 ? phase + windowMs : phase;
    }
}
