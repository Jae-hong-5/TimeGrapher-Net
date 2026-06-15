namespace TimeGrapher.Core.Shared;

/// <summary>
/// Standard watch test positions per NIHS 95-10 / ISO 3158, as drawn in the
/// project plan's "Indication of the test positions in accordance with
/// NIHS 95-10/ISO 3158" figure (Witschi Chronoscope X1 G3 manual, page 13).
/// The display uses the reference figure's DU/DD and crown-position labels.
/// The plan also requires "support for intermediate positions when used" and a
/// sequence of up to 10 positions, so the catalog includes the four diagonal
/// crown positions between the cardinal vertical positions.
/// Ordinals are stable 0..9 and double as array indices for the bounded
/// per-position aggregates.
/// </summary>
public enum WatchPosition
{
    /// <summary>Horizontal, dial up (cadran en haut).</summary>
    CH = 0,

    /// <summary>Horizontal, dial down (cadran en bas).</summary>
    CB = 1,

    /// <summary>Vertical, 6 o'clock up = crown left.</summary>
    P6H = 2,

    /// <summary>Vertical, 9 o'clock up = crown down.</summary>
    P9H = 3,

    /// <summary>Vertical, 3 o'clock up = crown up.</summary>
    P3H = 4,

    /// <summary>Vertical, 12 o'clock up = crown right.</summary>
    P12H = 5,

    /// <summary>Intermediate, 6 o'clock up tilted 45° toward dial-up.</summary>
    P6H45 = 6,

    /// <summary>Intermediate, 9 o'clock up tilted 45° toward dial-up.</summary>
    P9H45 = 7,

    /// <summary>Intermediate, 3 o'clock up tilted 45° toward dial-up.</summary>
    P3H45 = 8,

    /// <summary>Intermediate, 12 o'clock up tilted 45° toward dial-up.</summary>
    P12H45 = 9,
}

/// <summary>Display names and orientation classes of <see cref="WatchPosition"/>.</summary>
public static class WatchPositions
{
    /// <summary>Number of catalog positions (bounds per-position storage); the plan's "up to 10".</summary>
    public const int Count = 10;

    /// <summary>All positions in manual order (horizontal pair, verticals, then intermediates).</summary>
    public static readonly IReadOnlyList<WatchPosition> All = new[]
    {
        WatchPosition.CH,
        WatchPosition.CB,
        WatchPosition.P6H,
        WatchPosition.P9H,
        WatchPosition.P3H,
        WatchPosition.P12H,
        WatchPosition.P6H45,
        WatchPosition.P9H45,
        WatchPosition.P3H45,
        WatchPosition.P12H45,
    };

    /// <summary>Reference-figure designation shown in compact displays.</summary>
    public static string ShortName(this WatchPosition position) => position switch
    {
        WatchPosition.CH => "DU",
        WatchPosition.CB => "DD",
        WatchPosition.P6H => "CL",
        WatchPosition.P9H => "CD",
        WatchPosition.P3H => "CU",
        WatchPosition.P12H => "CR",
        WatchPosition.P6H45 => "CU(L)",
        WatchPosition.P9H45 => "CD(L)",
        WatchPosition.P3H45 => "CU(R)",
        WatchPosition.P12H45 => "CD(R)",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    /// <summary>Plain-language orientation for tooltips and status text.</summary>
    public static string LongName(this WatchPosition position) => position switch
    {
        WatchPosition.CH => "Dial up",
        WatchPosition.CB => "Dial down",
        WatchPosition.P6H => "Crown left",
        WatchPosition.P9H => "Crown down",
        WatchPosition.P3H => "Crown up",
        WatchPosition.P12H => "Crown right",
        WatchPosition.P6H45 => "Crown up-left",
        WatchPosition.P9H45 => "Crown down-left",
        WatchPosition.P3H45 => "Crown up-right",
        WatchPosition.P12H45 => "Crown down-right",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    /// <summary>True for the two flat positions (CH/CB); false for hanging and intermediate ones.</summary>
    public static bool IsHorizontal(this WatchPosition position) =>
        position is WatchPosition.CH or WatchPosition.CB;

    /// <summary>
    /// True for the 45° intermediate positions. They contribute to the sequence
    /// means/spreads but are excluded from the vertical-vs-horizontal comparison
    /// and the unbalance heuristic, which are defined over the full hanging
    /// positions only.
    /// </summary>
    public static bool IsIntermediate(this WatchPosition position) =>
        position is WatchPosition.P6H45 or WatchPosition.P9H45
            or WatchPosition.P3H45 or WatchPosition.P12H45;
}
