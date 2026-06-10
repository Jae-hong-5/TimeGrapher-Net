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
    public void AllListsTheSixStandardPositionsInManualOrder()
    {
        Assert.Equal(WatchPositions.Count, WatchPositions.All.Count);
        Assert.Equal(
            new[]
            {
                WatchPosition.CH, WatchPosition.CB, WatchPosition.P6H,
                WatchPosition.P9H, WatchPosition.P3H, WatchPosition.P12H,
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
    [InlineData(WatchPosition.CH, "CH", "Dial up", true)]
    [InlineData(WatchPosition.CB, "CB", "Dial down", true)]
    [InlineData(WatchPosition.P6H, "6H", "Crown left", false)]
    [InlineData(WatchPosition.P9H, "9H", "Crown down", false)]
    [InlineData(WatchPosition.P3H, "3H", "Crown up", false)]
    [InlineData(WatchPosition.P12H, "12H", "Crown right", false)]
    public void NamesAndOrientationFollowTheManualFigure(
        WatchPosition position, string shortName, string longName, bool horizontal)
    {
        Assert.Equal(shortName, position.ShortName());
        Assert.Equal(longName, position.LongName());
        Assert.Equal(horizontal, position.IsHorizontal());
    }
}
