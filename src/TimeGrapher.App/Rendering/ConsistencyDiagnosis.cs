using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>Status of one position-consistency requirement.</summary>
internal enum ConsistencyStatus
{
    Collecting,
    Ok,
    Check,
    Ready,
    Reference,
}

/// <summary>
/// Pure cross-position consistency diagnosis derived from a <see cref="SequenceSummary"/>
/// and the active position: the overall OK/CHECK/COLLECTING verdict plus the per-requirement
/// statuses and rate-spread readings. Extracted from the Positions renderer so the same
/// judgment can be consumed by more than one view (the Strategy "another consumer over one
/// snapshot" tactic) without duplicating the rule. Holds no Avalonia types, so it is
/// unit-testable without a window.
/// </summary>
internal sealed record ConsistencyDiagnosis(
    VarioVerdictLevel Level,
    string VerdictText,
    string DetailText,
    ConsistencyStatus SpreadStatus,
    ConsistencyStatus BalanceStatus,
    ConsistencyStatus VerticalHorizontalStatus,
    double? RateSpreadSPerDay,
    double? VerticalRateSpreadSPerDay,
    double? VerticalHorizontalRateDeltaSPerDay)
{
    /// <summary>Beats a position needs before it counts toward a verdict.</summary>
    public const long MinPositionBeatsForVerdict = VarioVerdict.MinSamples;

    /// <summary>Qualified positions needed before the spread verdict is offered.</summary>
    public const int MinQualifiedPositionsForVerdict = 3;

    /// <summary>Full vertical positions needed before the balance-wheel verdict is offered.</summary>
    public const int MinVerticalPositionsForBalanceWheelVerdict = 2;

    public static ConsistencyDiagnosis Compute(SequenceSummary summary, WatchPosition activePosition)
    {
        double threshold = SequenceSummary.UnbalanceVerticalRateSpreadSPerDay;

        SequencePositionRow[] qualifiedRows = summary.Rows
            .Where(row => row.RateSPerDay != null && row.Beats >= MinPositionBeatsForVerdict)
            .ToArray();
        SequencePositionRow[] fullVerticalRows = qualifiedRows
            .Where(row => !row.Position.IsHorizontal() && !row.Position.IsIntermediate())
            .ToArray();
        SequencePositionRow[] horizontalRows = qualifiedRows
            .Where(row => row.Position.IsHorizontal())
            .ToArray();

        double? qualifiedRateSpread = Spread(qualifiedRows.Select(row => row.RateSPerDay!.Value));
        double? qualifiedVerticalSpread = Spread(fullVerticalRows.Select(row => row.RateSPerDay!.Value));

        // Per-requirement statuses (the Positions guide rows).
        ConsistencyStatus spreadStatus = qualifiedRows.Length < MinQualifiedPositionsForVerdict
            ? ConsistencyStatus.Collecting
            : qualifiedRateSpread > threshold ? ConsistencyStatus.Check : ConsistencyStatus.Ok;
        ConsistencyStatus balanceStatus = fullVerticalRows.Length < MinVerticalPositionsForBalanceWheelVerdict
            ? ConsistencyStatus.Collecting
            : qualifiedVerticalSpread > threshold ? ConsistencyStatus.Check : ConsistencyStatus.Ok;
        ConsistencyStatus verticalHorizontalStatus =
            fullVerticalRows.Length < 1 || horizontalRows.Length < 1
                ? ConsistencyStatus.Collecting
                : ConsistencyStatus.Ready;

        // Overall verdict: gate on the active position, then on enough qualified
        // positions (with the balance-wheel 2V+1H requirement) before grading the spread.
        SequencePositionRow? activeRow = summary.Rows.FirstOrDefault(row => row.Position == activePosition);
        long activeBeats = activeRow?.Beats ?? 0;

        VarioVerdictLevel level;
        string verdict;
        string detail;
        if (activeRow?.RateSPerDay == null || activeBeats < MinPositionBeatsForVerdict)
        {
            level = VarioVerdictLevel.Pending;
            verdict = "COLLECTING";
            detail = $"Measuring {activePosition.ShortName()}: {activeBeats}/{MinPositionBeatsForVerdict} beats.";
        }
        else if (qualifiedRows.Length < MinQualifiedPositionsForVerdict)
        {
            level = VarioVerdictLevel.Pending;
            verdict = "COLLECTING";
            detail = $"Measure another position to {MinPositionBeatsForVerdict} beats.";
        }
        else if (fullVerticalRows.Length < MinVerticalPositionsForBalanceWheelVerdict || horizontalRows.Length < 1)
        {
            level = VarioVerdictLevel.Pending;
            verdict = "COLLECTING";
            detail = "Measure full vertical and horizontal positions.";
        }
        else if (qualifiedRateSpread > threshold || qualifiedVerticalSpread > threshold)
        {
            level = VarioVerdictLevel.Warn;
            verdict = "CHECK";
            detail = $"Rate spread exceeds {threshold:0} s/d.";
        }
        else
        {
            level = VarioVerdictLevel.Good;
            verdict = "OK";
            detail = $"Rate spread within {threshold:0} s/d.";
        }

        return new ConsistencyDiagnosis(
            level,
            verdict,
            detail,
            spreadStatus,
            balanceStatus,
            verticalHorizontalStatus,
            qualifiedRateSpread,
            qualifiedVerticalSpread,
            summary.VerticalHorizontalRateDeltaSPerDay);
    }

    private static double? Spread(IEnumerable<double> values)
    {
        double[] snapshot = values.ToArray();
        if (snapshot.Length < 2)
        {
            return null;
        }

        return snapshot.Max() - snapshot.Min();
    }
}
