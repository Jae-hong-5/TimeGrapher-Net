namespace TimeGrapher.Core.Shared;

/// <summary>
/// Standard watch test positions per NIHS 95-10 / ISO 3158, as drawn in the
/// project plan's "Indication of the test positions in accordance with
/// NIHS 95-10/ISO 3158" figure (Witschi Chronoscope X1 G3 manual, page 13).
/// The two horizontal positions are named after the dial (cadran): CH = dial
/// up, CB = dial down. The four vertical (hanging) positions are named after
/// the hour index that points up; with the crown on the 3 o'clock side of the
/// case the figure's crown pictograms therefore map to: 6H up = crown left,
/// 9H up = crown down, 3H up = crown up, 12H up = crown right.
/// Ordinals are stable 0..5 and double as array indices for the bounded
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
}

/// <summary>Display names and orientation classes of <see cref="WatchPosition"/>.</summary>
public static class WatchPositions
{
    /// <summary>Number of standard positions (bounds per-position storage).</summary>
    public const int Count = 6;

    /// <summary>All standard positions in manual order (horizontal pair first).</summary>
    public static readonly IReadOnlyList<WatchPosition> All = new[]
    {
        WatchPosition.CH,
        WatchPosition.CB,
        WatchPosition.P6H,
        WatchPosition.P9H,
        WatchPosition.P3H,
        WatchPosition.P12H,
    };

    /// <summary>NIHS designation shown in compact displays ("CH", "6H", ...).</summary>
    public static string ShortName(this WatchPosition position) => position switch
    {
        WatchPosition.CH => "CH",
        WatchPosition.CB => "CB",
        WatchPosition.P6H => "6H",
        WatchPosition.P9H => "9H",
        WatchPosition.P3H => "3H",
        WatchPosition.P12H => "12H",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    /// <summary>Plain-language orientation ("Dial up", "Crown left", ...).</summary>
    public static string LongName(this WatchPosition position) => position switch
    {
        WatchPosition.CH => "Dial up",
        WatchPosition.CB => "Dial down",
        WatchPosition.P6H => "Crown left",
        WatchPosition.P9H => "Crown down",
        WatchPosition.P3H => "Crown up",
        WatchPosition.P12H => "Crown right",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    /// <summary>True for the two flat positions (CH/CB); false for the hanging ones.</summary>
    public static bool IsHorizontal(this WatchPosition position) =>
        position is WatchPosition.CH or WatchPosition.CB;
}
