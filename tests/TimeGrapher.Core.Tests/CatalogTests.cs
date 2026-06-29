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
        // Pin the FULL ordered manual-rate sequence (not just membership of a couple
        // of antique rates): a reordered, dropped, or added rate must fail here.
        Assert.Equal(
            new[]
            {
                 3600,  6000,  7200,  7380,  7440,  7800,  9000,  9100, 10800, 11880,
                12000, 12342, 12480, 12600, 13320, 13440, 13500, 14000, 14040, 14160,
                14200, 14280, 14400, 14520, 14580, 14760, 14850, 15000, 15360, 15600,
                16200, 16320, 16800, 17196, 17258, 17280, 17786, 17897, 18000, 18049,
                18514, 19332, 19440, 19800, 20160, 20222, 20944, 21000, 21031, 21306,
                21600, 25200, 28800, 32400, 36000, 43200,
            },
            BphCatalog.ManualBph);
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
