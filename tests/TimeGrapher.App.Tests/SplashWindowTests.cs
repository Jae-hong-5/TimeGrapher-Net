using TimeGrapher.App.Views;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SplashWindowTests
{
    [Fact]
    public void FrameSelectionFollowsElapsedThirtyFpsTime()
    {
        Assert.Equal(1, SplashWindow.GetFrameNumberForElapsed(TimeSpan.Zero));
        Assert.Equal(1, SplashWindow.GetFrameNumberForElapsed(TimeSpan.FromSeconds(-1.0)));
        Assert.Equal(2, SplashWindow.GetFrameNumberForElapsed(TimeSpan.FromSeconds(1.0 / 30.0)));
        Assert.Equal(31, SplashWindow.GetFrameNumberForElapsed(TimeSpan.FromSeconds(1.0)));
        Assert.Equal(74, SplashWindow.GetFrameNumberForElapsed(TimeSpan.FromSeconds(74.0 / 30.0)));
        Assert.Equal(74, SplashWindow.GetFrameNumberForElapsed(TimeSpan.FromSeconds(10.0)));
    }
}
