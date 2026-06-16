using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// NIHS 95-10 / ISO 3158 test-position catalog: names, orientation classes and
/// the stable ordinals the per-position aggregates index by.
/// </summary>
public sealed class WatchPositionsTests
{
    [Fact]
    public void AllListsTheTenPositionsInManualOrder()
    {
        // 6 standard NIHS positions + 4 intermediate 45° positions = the
        // plan's "up to 10 test positions" sequence capacity.
        Assert.Equal(WatchPositions.Count, WatchPositions.All.Count);
        Assert.Equal(
            new[]
            {
                WatchPosition.CH, WatchPosition.CB, WatchPosition.P6H,
                WatchPosition.P9H, WatchPosition.P3H, WatchPosition.P12H,
                WatchPosition.P6H45, WatchPosition.P9H45,
                WatchPosition.P3H45, WatchPosition.P12H45,
            },
            WatchPositions.All);
    }

    [Fact]
    public void OrdinalsAreStableArrayIndices()
    {
        for (int i = 0; i < WatchPositions.All.Count; i++)
        {
            Assert.Equal(i, (int)WatchPositions.All[i]);
        }
    }

    [Theory]
    [InlineData(WatchPosition.CH, "DU", "Dial up", true, false)]
    [InlineData(WatchPosition.CB, "DD", "Dial down", true, false)]
    [InlineData(WatchPosition.P6H, "CL", "Crown left", false, false)]
    [InlineData(WatchPosition.P9H, "CD", "Crown down", false, false)]
    [InlineData(WatchPosition.P3H, "CU", "Crown up", false, false)]
    [InlineData(WatchPosition.P12H, "CR", "Crown right", false, false)]
    [InlineData(WatchPosition.P6H45, "CU(L)", "Crown up-left", false, true)]
    [InlineData(WatchPosition.P9H45, "CD(L)", "Crown down-left", false, true)]
    [InlineData(WatchPosition.P3H45, "CU(R)", "Crown up-right", false, true)]
    [InlineData(WatchPosition.P12H45, "CD(R)", "Crown down-right", false, true)]
    public void NamesAndOrientationFollowTheManualFigure(
        WatchPosition position, string shortName, string longName, bool horizontal, bool intermediate)
    {
        Assert.Equal(shortName, position.ShortName());
        Assert.Equal(longName, position.LongName());
        Assert.Equal(horizontal, position.IsHorizontal());
        Assert.Equal(intermediate, position.IsIntermediate());
    }
}
