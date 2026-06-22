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
    public void AllListsTheTenPositionsInProposedDisplayOrder()
    {
        // 6 standard NIHS positions + 4 intermediate 45° positions = the
        // plan's "up to 10 watch positions" sequence capacity.
        Assert.Equal(WatchPositions.Count, WatchPositions.All.Count);
        Assert.Equal(
            new[]
            {
                WatchPosition.CH, WatchPosition.CB, WatchPosition.P12H,
                WatchPosition.P3H45, WatchPosition.P3H, WatchPosition.P6H45,
                WatchPosition.P6H, WatchPosition.P9H45,
                WatchPosition.P9H, WatchPosition.P12H45,
            },
            WatchPositions.All);
    }

    [Fact]
    public void OrdinalsRemainStableArrayIndices()
    {
        Assert.Equal(0, (int)WatchPosition.CH);
        Assert.Equal(1, (int)WatchPosition.CB);
        Assert.Equal(2, (int)WatchPosition.P6H);
        Assert.Equal(3, (int)WatchPosition.P9H);
        Assert.Equal(4, (int)WatchPosition.P3H);
        Assert.Equal(5, (int)WatchPosition.P12H);
        Assert.Equal(6, (int)WatchPosition.P6H45);
        Assert.Equal(7, (int)WatchPosition.P9H45);
        Assert.Equal(8, (int)WatchPosition.P3H45);
        Assert.Equal(9, (int)WatchPosition.P12H45);
    }

    [Theory]
    [InlineData(WatchPosition.CH, "CH", "Dial up", true, false)]
    [InlineData(WatchPosition.CB, "CB", "Dial down", true, false)]
    [InlineData(WatchPosition.P6H, "6H", "6:00 up", false, false)]
    [InlineData(WatchPosition.P9H, "9H", "9:00 up", false, false)]
    [InlineData(WatchPosition.P3H, "3H", "3:00 up", false, false)]
    [InlineData(WatchPosition.P12H, "12H", "12:00 up", false, false)]
    [InlineData(WatchPosition.P6H45, "4:30H", "4:30 up", false, true)]
    [InlineData(WatchPosition.P9H45, "7:30H", "7:30 up", false, true)]
    [InlineData(WatchPosition.P3H45, "1:30H", "1:30 up", false, true)]
    [InlineData(WatchPosition.P12H45, "10:30H", "10:30 up", false, true)]
    public void NamesAndOrientationFollowProposedTerminology(
        WatchPosition position, string shortName, string longName, bool horizontal, bool intermediate)
    {
        Assert.Equal(shortName, position.ShortName());
        Assert.Equal(longName, position.LongName());
        Assert.Equal(horizontal, position.IsHorizontal());
        Assert.Equal(intermediate, position.IsIntermediate());
    }
}
