using System;
using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Constructor argument validation for <see cref="TgDetector"/>. The integration
/// scenarios always build a valid Auto-mode config, so these guards (sample rate
/// and manual-BPH membership) would otherwise go unexercised.
/// </summary>
public sealed class TgDetectorConfigTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveSampleRate()
    {
        TgConfig cfg = TgConfig.Default();
        cfg.SampleRate = 0.0;
        Assert.Throws<ArgumentException>(() => new TgDetector(cfg));
    }

    [Fact]
    public void Constructor_RejectsManualModeWithBphNotInCatalog()
    {
        TgConfig cfg = TgConfig.Default();
        cfg.BphMode = TgBphMode.Manual;
        cfg.ManualBph = 12345; // deliberately not a catalogued antique rate
        Assert.False(Bph.IsValidManualBph(cfg.ManualBph));
        Assert.Throws<ArgumentException>(() => new TgDetector(cfg));
    }

    [Fact]
    public void Constructor_AcceptsManualModeWithCataloguedBph()
    {
        TgConfig cfg = TgConfig.Default();
        cfg.BphMode = TgBphMode.Manual;
        cfg.ManualBph = Bph.ManualBphList[0];
        Assert.True(Bph.IsValidManualBph(cfg.ManualBph));

        var detector = new TgDetector(cfg); // must not throw
        Assert.NotNull(detector);
    }
}
