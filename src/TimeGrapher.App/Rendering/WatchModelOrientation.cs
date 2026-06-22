using System.Numerics;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Maps each <see cref="WatchPosition"/> to the orientation of the watch model,
/// and supplies the transition timing the model file recommends.
///
/// The model's reference frame (per <c>watch_model_round/README_KO.md</c>): the
/// dial faces <c>+Z</c>, 12 o'clock is <c>+Y</c>, the crown / 3 o'clock is
/// <c>+X</c>, and the rotation pivot is the case centre <c>(0,0,0)</c>.
///
/// The display favours readability over the literal lab geometry of
/// <c>watch_positions.json</c> (whose signs it notes "may need adjustment
/// depending on the camera"), and distinguishes the two orientation classes the
/// way the bench does:
///
/// • The hanging/vertical positions (12H, 3H, 6H, 9H and the four 45°
///   intermediates) stand the watch up facing the viewer and spin it in-plane
///   about the viewing axis (<c>+Z</c>) so the named hour sits at the top —
///   3 o'clock up puts the 3 marker at top, etc.
///
/// • The two horizontal positions lie the watch flat so it reads as a side
///   profile, not a head-on dial: CH (dial up / cadran en haut) tips −90° about
///   X so the dial faces up, and CB (dial down) tips +90° about X so the
///   caseback faces up. Seen by the slightly raised camera, both show the flat
///   case edge-on, which is exactly what tells them apart from the standing
///   positions.
/// </summary>
internal static class WatchModelOrientation
{
    /// <summary>Transition duration, from <c>watch_positions.json</c> (<c>recommendedDurationMs</c>).</summary>
    public const double TransitionMilliseconds = 650.0;

    public static Quaternion For(WatchPosition position) => position switch
    {
        WatchPosition.CH => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f),
        WatchPosition.CB => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
        WatchPosition.P12H => HourUp(12f),
        WatchPosition.P3H45 => HourUp(1.5f),
        WatchPosition.P3H => HourUp(3f),
        WatchPosition.P6H45 => HourUp(4.5f),
        WatchPosition.P6H => HourUp(6f),
        WatchPosition.P9H45 => HourUp(7.5f),
        WatchPosition.P9H => HourUp(9f),
        WatchPosition.P12H45 => HourUp(10.5f),
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    /// <summary>
    /// Dial-facing orientation that brings the given clock hour to the top: an
    /// in-plane spin about the viewing axis (<c>+Z</c>) by the hour's angle from
    /// 12 o'clock (each hour = 30°). Viewed from the front camera this is a
    /// counter-clockwise turn, so the 3 o'clock marker (model <c>+X</c>, screen
    /// right) rises to the top after a 90° turn.
    /// </summary>
    private static Quaternion HourUp(float clockHours)
    {
        float angle = clockHours * 30f * (MathF.PI / 180f);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
    }

    /// <summary>
    /// Cubic ease-in-out (the easing <c>watch_positions.json</c> recommends),
    /// clamped to [0, 1]. Slow at both ends, fastest at the midpoint.
    /// </summary>
    public static double CubicEaseInOut(double t)
    {
        if (t <= 0.0)
        {
            return 0.0;
        }

        if (t >= 1.0)
        {
            return 1.0;
        }

        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }
}
