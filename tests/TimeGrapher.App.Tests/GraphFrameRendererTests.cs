using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class GraphFrameRendererTests
{
    [Fact]
    public void PlaceholderResultsMatchesFixedWidthLayout()
    {
        // Must stay byte-for-byte aligned with WatchMetrics.BuildResults(all-invalid) so the
        // readout does not shift when the first real metrics arrive.
        Assert.Equal(
            "RATE ------ s/d | AMPLITUDE ---° | BEAT ERROR ---- ms | BEAT ----- bph",
            GraphFrameRenderer.PlaceholderResults);
    }
}
