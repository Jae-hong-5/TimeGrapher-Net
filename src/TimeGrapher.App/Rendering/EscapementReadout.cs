using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure formatting behind the Escapement Analyzer marker labels and numeric
/// panel, kept out of the renderer so it is unit-testable without a live
/// control tree: the current beat's A→C readings per timing reference, the
/// onset-vs-peak delta, the windowed mean±sigma of both references and the
/// repeatability verdict (the plan's "which reference point produces the most
/// stable timing results").
/// </summary>
internal static class EscapementReadout
{
    private const string SignedTenthsMsFormat = "+0.0;-0.0;0.0";
    private const string SignedHundredthsMsFormat = "+0.00;-0.00;0.00";

    /// <summary>
    /// A marks each beat's cycle start; the millisecond reading lives on that
    /// beat's C label (the A→C interval), so the A marker carries just the
    /// event name — shared by both the tic and toc A markers.
    /// </summary>
    public const string AMarkerLabel = "A";

    /// <summary>Panel labels; <see cref="Values"/> returns matching positions.</summary>
    public static readonly string[] Labels =
    {
        "A→C PEAK", "A→C ONSET", "ONSET-PEAK",
        "PEAK MEAN±σ", "ONSET MEAN±σ", "MORE REPEATABLE",
    };

    public static string CPeakMarkerLabel(double aToCPeakMs) =>
        "C peak " + aToCPeakMs.ToString(SignedTenthsMsFormat, CultureInfo.InvariantCulture) + " ms";

    public static string COnsetMarkerLabel(double aToCOnsetMs) =>
        "C onset " + aToCOnsetMs.ToString(SignedTenthsMsFormat, CultureInfo.InvariantCulture) + " ms";

    /// <summary>Formatted readings in <see cref="Labels"/> order (em dash when absent).</summary>
    public static string[] Values(BeatSegment? latest, EscapementTimingTracker tracker)
    {
        double? peakMs = latest is { CPeakValid: true }
            ? latest.CPeakOffsetMs - latest.AOffsetMs
            : null;
        double? onsetMs = latest is { COnsetValid: true }
            ? latest.COnsetOffsetMs - latest.AOffsetMs
            : null;

        return new[]
        {
            VarioReadout.Format(peakMs, SignedTenthsMsFormat, " ms"),
            VarioReadout.Format(onsetMs, SignedTenthsMsFormat, " ms"),
            VarioReadout.Format(onsetMs - peakMs, SignedHundredthsMsFormat, " ms"),
            MeanSigma(tracker.PeakMeanMs, tracker.PeakSigmaMs, tracker.PeakCount),
            MeanSigma(tracker.OnsetMeanMs, tracker.OnsetSigmaMs, tracker.OnsetCount),
            SignalAwareVerdict(latest, tracker),
        };
    }

    private static string SignalAwareVerdict(BeatSegment? latest, EscapementTimingTracker tracker)
    {
        if (latest is { Quality: not SignalQualityFlags.None } segment)
        {
            return SignalQualityText.Summary(segment.Quality);
        }

        return VerdictLabel(tracker.Verdict);
    }

    public static string VerdictLabel(EscapementReferenceVerdict verdict) => verdict switch
    {
        EscapementReferenceVerdict.Onset => "ONSET",
        EscapementReferenceVerdict.Peak => "PEAK",
        _ => VarioReadout.Missing,
    };

    private static string MeanSigma(double? meanMs, double? sigmaMs, int count) =>
        meanMs is double mean && sigmaMs is double sigma
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.0} ±{1:0.00} ms (n={2})", mean, sigma, count)
            : VarioReadout.Missing;
}
