namespace TimeGrapher.Core.Shared;

/// <summary>
/// Standard watch positions per NIHS 95-10 / ISO 3158, as drawn in the
/// project plan's "Indication of the watch positions in accordance with
/// NIHS 95-10/ISO 3158" figure (Witschi Chronoscope X1 G3 manual, page 13).
/// The display uses the NIHS/Witschi CH/CB/hour-position labels.
/// The plan also requires "support for intermediate positions when used" and a
/// sequence of up to 10 positions, so the catalog includes the four diagonal
/// hour positions between the cardinal vertical positions.
/// Ordinals are stable 0..9 and double as array indices for the bounded
/// per-position aggregates.
/// </summary>
public enum WatchPosition
{
    /// <summary>Horizontal, dial up (cadran en haut).</summary>
    CH = 0,

    /// <summary>Horizontal, dial down (cadran en bas).</summary>
    CB = 1,

    /// <summary>Vertical, 6 o'clock up.</summary>
    P6H = 2,

    /// <summary>Vertical, 9 o'clock up.</summary>
    P9H = 3,

    /// <summary>Vertical, 3 o'clock up.</summary>
    P3H = 4,

    /// <summary>Vertical, 12 o'clock up.</summary>
    P12H = 5,

    /// <summary>Intermediate diagonal: 4:30 up.</summary>
    P6H45 = 6,

    /// <summary>Intermediate diagonal: 7:30 up.</summary>
    P9H45 = 7,

    /// <summary>Intermediate diagonal: 1:30 up.</summary>
    P3H45 = 8,

    /// <summary>Intermediate diagonal: 10:30 up.</summary>
    P12H45 = 9,
}

/// <summary>Display names and orientation classes of <see cref="WatchPosition"/>.</summary>
public static class WatchPositions
{
    /// <summary>Number of catalog positions (bounds per-position storage); the plan's "up to 10".</summary>
    public const int Count = 10;

    /// <summary>All positions in proposed display order, with enum ordinals kept stable for storage.</summary>
    public static readonly IReadOnlyList<WatchPosition> All = new[]
    {
        WatchPosition.CH,
        WatchPosition.CB,
        WatchPosition.P12H,
        WatchPosition.P3H45,
        WatchPosition.P3H,
        WatchPosition.P6H45,
        WatchPosition.P6H,
        WatchPosition.P9H45,
        WatchPosition.P9H,
        WatchPosition.P12H45,
    };

    /// <summary>NIHS/Witschi designation shown in compact displays.</summary>
    public static string ShortName(this WatchPosition position) => position switch
    {
        WatchPosition.CH => "CH",
        WatchPosition.CB => "CB",
        WatchPosition.P6H => "6H",
        WatchPosition.P9H => "9H",
        WatchPosition.P3H => "3H",
        WatchPosition.P12H => "12H",
        WatchPosition.P6H45 => "4:30H",
        WatchPosition.P9H45 => "7:30H",
        WatchPosition.P3H45 => "1:30H",
        WatchPosition.P12H45 => "10:30H",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    /// <summary>Plain-language orientation for tooltips and status text.</summary>
    public static string LongName(this WatchPosition position) => position switch
    {
        WatchPosition.CH => "Dial up",
        WatchPosition.CB => "Dial down",
        WatchPosition.P6H => "6 o'clock up",
        WatchPosition.P9H => "9 o'clock up",
        WatchPosition.P3H => "3 o'clock up",
        WatchPosition.P12H => "12 o'clock up",
        WatchPosition.P6H45 => "4:30 up",
        WatchPosition.P9H45 => "7:30 up",
        WatchPosition.P3H45 => "1:30 up",
        WatchPosition.P12H45 => "10:30 up",
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
