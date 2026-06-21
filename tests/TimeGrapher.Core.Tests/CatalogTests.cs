using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void ManualAutoBphAddsAutoEntryBeforeManualRates()
    {
        Assert.Equal(0, BphCatalog.ManualAutoBph[0]);
        // Assert the FULL manual sequence follows the auto sentinel in order; a
        // prefix/contains check would miss a dropped or reordered later rate.
        Assert.Equal(BphCatalog.ManualBph, BphCatalog.ManualAutoBph.Skip(1));
    }

    [Fact]
    public void ManualBphCatalogUsesDetectorRates()
    {
        Assert.Contains(17258, BphCatalog.ManualBph);
        Assert.Contains(17786, BphCatalog.ManualBph);
    }

    [Fact]
    public void StandardSampleRatesAreSharedByPlaybackAndCapture()
    {
        Assert.Equal(new[] { 48000, 96000, 192000 }, AudioSampleRates.Standard);
        // Full set equality (not just membership) so the derived set cannot carry
        // an extra rate or drop one relative to the shared standard list.
        Assert.True(AudioSampleRates.StandardSet.SetEquals(AudioSampleRates.Standard));
    }
}
