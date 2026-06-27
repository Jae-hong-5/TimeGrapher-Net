using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>Severity of a Vario assessment, used to colour the status chips and conclusion.</summary>
internal enum VarioVerdictLevel
{
    Pending,
    Good,
    Warn,
    Bad,
}

/// <summary>
/// The specific condition behind a verdict, so <see cref="VarioVerdict.Overall"/> can
/// name the fault and the matching Witschi service step instead of only its severity.
/// In-band / healthy / measuring readings carry <see cref="None"/>.
/// </summary>
internal enum VarioFinding
{
    None,
    RateFast,
    RateSlow,
    AmplitudeLow,
    AmplitudeSlightlyLow,
    AmplitudeHigh,
}

/// <summary>
/// Derives a short plain-language assessment of a Vario measure from its running
/// statistics and acceptable band. Pure and threshold-based, so any combination
/// of readings maps to one of a small, testable set of verdicts; it reads the
/// already-accumulated stats, so there is no per-beat cost. Because the stats are
/// per watch position, the verdict reflects the current position only.
/// </summary>
internal readonly record struct VarioVerdict(string Text, VarioVerdictLevel Level, VarioFinding Finding = VarioFinding.None)
{
    /// <summary>Beats required before a verdict is offered; fewer reads as "measuring".</summary>
    public const long MinSamples = 30;

    /// <summary>
    /// Mean amplitude (deg) below which low amplitude is flagged for service. Anchored to
    /// the Witschi training course, which lists vertical-position acceptable amplitude as
    /// 220–270°: below 220° the watch is sub-nominal in every position, so it is flagged
    /// for service (clean &amp; lubricate / overhaul). Amplitude also declines with oil aging.
    /// </summary>
    public const double AmplitudeServiceDeg = 220.0;

    public static readonly VarioVerdict Measuring = new("Measuring…", VarioVerdictLevel.Pending);

    /// <summary>
    /// Error Rate (s/d): whether the mean daily rate is inside the acceptable band or
    /// running fast/slow. Stability within the band is left to the displayed σ (Std Dev)
    /// readout rather than a fixed cutoff — Witschi shows σ but defines no single-position
    /// stability threshold, and the standard rate-variation tolerances (COSC/ISO 3159) are
    /// across positions, which the Positions/Sequence view covers, not Vario.
    /// </summary>
    public static VarioVerdict ForRate(StatsSummary stats, double acceptMin, double acceptMax)
    {
        if (!stats.Valid || stats.Count < MinSamples)
        {
            return Measuring;
        }

        if (stats.Mean > acceptMax)
        {
            return new VarioVerdict("Fast · out of range", VarioVerdictLevel.Bad, VarioFinding.RateFast);
        }

        if (stats.Mean < acceptMin)
        {
            return new VarioVerdict("Slow · out of range", VarioVerdictLevel.Bad, VarioFinding.RateSlow);
        }

        return new VarioVerdict("Within Band", VarioVerdictLevel.Good);
    }

    /// <summary>Amplitude (deg): healthy band, marginally low/high, or low enough to flag service.</summary>
    public static VarioVerdict ForAmplitude(StatsSummary stats, double healthyMin, double healthyMax)
    {
        if (!stats.Valid || stats.Count < MinSamples)
        {
            return Measuring;
        }

        if (stats.Mean < AmplitudeServiceDeg)
        {
            return new VarioVerdict("Low · service", VarioVerdictLevel.Bad, VarioFinding.AmplitudeLow);
        }

        if (stats.Mean < healthyMin)
        {
            return new VarioVerdict("Slightly low", VarioVerdictLevel.Warn, VarioFinding.AmplitudeSlightlyLow);
        }

        if (stats.Mean > healthyMax)
        {
            return new VarioVerdict("High", VarioVerdictLevel.Warn, VarioFinding.AmplitudeHigh);
        }

        return new VarioVerdict("Healthy", VarioVerdictLevel.Good);
    }

    /// <summary>
    /// Combined one-line conclusion across both measures: it takes the worse severity
    /// and, for that severity, names the specific fault(s) and the matching Witschi
    /// service step — so the conclusion says what to do, not just how serious it is.
    /// </summary>
    public static VarioVerdict Overall(VarioVerdict rate, VarioVerdict amplitude)
    {
        if (rate.Level == VarioVerdictLevel.Pending || amplitude.Level == VarioVerdictLevel.Pending)
        {
            return new VarioVerdict(string.Empty, VarioVerdictLevel.Pending);
        }

        var level = (VarioVerdictLevel)Math.Max((int)rate.Level, (int)amplitude.Level);
        if (level == VarioVerdictLevel.Good)
        {
            return new VarioVerdict("Overall: OK · Within band — ready to record", level);
        }

        // List only the measure(s) at the worst severity (the others are already shown
        // in their own chips), each with its repair-oriented action.
        var clauses = new List<string>(2);
        foreach (VarioVerdict measure in new[] { rate, amplitude })
        {
            if (measure.Level == level && ActionFor(measure.Finding) is { Length: > 0 } clause)
            {
                clauses.Add(clause);
            }
        }

        string faults = string.Join(" · ", clauses);
        return level == VarioVerdictLevel.Bad
            ? new VarioVerdict($"Overall: ALERT · {faults} before recording", level)
            : new VarioVerdict($"Overall: WATCH · {faults} · keep measuring", level);
    }

    /// <summary>The Witschi-grounded repair step for a fault, used to build the conclusion.</summary>
    private static string ActionFor(VarioFinding finding) => finding switch
    {
        VarioFinding.RateFast => "Running fast — regulate slower",
        VarioFinding.RateSlow => "Running slow — regulate faster",
        VarioFinding.AmplitudeLow => "Low amplitude — service (clean & lubricate)",
        VarioFinding.AmplitudeSlightlyLow => "Amplitude low — service soon",
        VarioFinding.AmplitudeHigh => "High amplitude — check mainspring/escapement",
        _ => string.Empty,
    };
}
