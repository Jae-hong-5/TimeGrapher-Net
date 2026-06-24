using System.Numerics;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The dial-facing orientation contract: every position keeps the dial toward
/// the viewer and spins it in-plane so the named hour is at the top, except CB
/// which flips to the caseback. Verified by where each orientation sends the
/// model's reference axes (dial normal <c>+Z</c>, 12 o'clock <c>+Y</c>, crown /
/// 3 o'clock <c>+X</c>).
/// </summary>
public sealed class WatchModelOrientationTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void DialUpLiesFlatWithTheDialFacingUp()
    {
        // CH is horizontal: the dial normal (+Z) tips up to +Y so the flat watch
        // reads as a side profile (dial up), not a head-on dial.
        AssertAxis(WatchPosition.CH, Vector3.UnitZ, new Vector3(0, 1, 0));
    }

    [Fact]
    public void DialDownLiesFlatWithTheCasebackFacingUp()
    {
        // CB is horizontal: the dial faces down (−Y), so the raised camera sees
        // the caseback of the flat watch.
        AssertAxis(WatchPosition.CB, Vector3.UnitZ, new Vector3(0, -1, 0));
    }

    [Fact]
    public void TwelveOclockUpIsTheHeadOnStandingDial()
    {
        // "12 o'clock up" is the canonical head-on standing view: the identity
        // rotation with the 12 marker at top. (The 360° turn about Z yields the
        // antipodal q ≈ (0,0,0,−1) — the same rotation — so compare up to sign.)
        Quaternion twelve = WatchModelOrientation.For(WatchPosition.P12H);
        Assert.True(MathF.Abs(Quaternion.Dot(twelve, Quaternion.Identity)) > 0.9999f);
        AssertAxis(WatchPosition.P12H, Vector3.UnitY, new Vector3(0, 1, 0));
    }

    [Theory]
    // The crown sits at 3 o'clock (model +X); "hour H up" must raise the H
    // marker to the top (+Y). Tracking +X shows the crown's screen position.
    [InlineData(WatchPosition.P3H, 1f, 0f, 0f, 0f, 1f, 0f)]   // 3 up → crown to top
    [InlineData(WatchPosition.P9H, 1f, 0f, 0f, 0f, -1f, 0f)]  // 9 up → crown to bottom
    [InlineData(WatchPosition.P6H, 0f, 1f, 0f, 0f, -1f, 0f)]  // 6 up → 12 marker to bottom
    public void HourUpRotatesTheNamedHourToTheTop(
        WatchPosition position,
        float sx, float sy, float sz,
        float ex, float ey, float ez)
    {
        AssertAxis(position, new Vector3(sx, sy, sz), new Vector3(ex, ey, ez));
    }

    [Fact]
    public void IntermediatePositionSitsHalfwayBetweenItsNeighbours()
    {
        // 1:30 up is a 45° in-plane spin, so the crown (+X) lands on the +X/+Y diagonal.
        float h = MathF.Sqrt(0.5f);
        AssertAxis(WatchPosition.P3H45, Vector3.UnitX, new Vector3(h, h, 0f));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void CubicEaseInOutClampsAndAnchorsItsEnds(double t, double expected)
    {
        Assert.Equal(expected, WatchModelOrientation.CubicEaseInOut(t), 6);
    }

    [Fact]
    public void CubicEaseInOutIsMonotonicAndSymmetric()
    {
        Assert.True(WatchModelOrientation.CubicEaseInOut(0.25) < WatchModelOrientation.CubicEaseInOut(0.75));
        // Symmetric about the midpoint: e(t) + e(1−t) = 1.
        Assert.Equal(1.0, WatchModelOrientation.CubicEaseInOut(0.3) + WatchModelOrientation.CubicEaseInOut(0.7), 6);
    }

    private static void AssertAxis(WatchPosition position, Vector3 modelAxis, Vector3 expected)
    {
        Vector3 actual = Vector3.Transform(modelAxis, WatchModelOrientation.For(position));
        Assert.True(
            (actual - expected).Length() < Tolerance,
            $"{position}: expected {expected}, got {actual}");
    }
}
