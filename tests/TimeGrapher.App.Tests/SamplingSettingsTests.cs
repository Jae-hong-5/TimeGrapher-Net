using TimeGrapher.App;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the sampling-parameter validity + normalization rules: both values must be in
/// range AND on their step grid, and any raw input snap-and-clamps to a usable value.
/// These are the gate the controller, the store fallback, and the run-start boundary all
/// rely on, so they are pinned independently of the window.
/// </summary>
public sealed class SamplingSettingsTests
{
    [Theory]
    [InlineData(4096, 20, 10, true)]    // Default
    [InlineData(256, 5, 1, true)]       // floors
    [InlineData(16384, 200, 240, true)] // ceilings
    [InlineData(255, 20, 20, false)]    // block below floor
    [InlineData(16385, 20, 20, false)]  // block above ceiling
    [InlineData(257, 20, 20, false)]    // block off-step
    [InlineData(4096, 4, 20, false)]    // buffer below floor
    [InlineData(4096, 201, 20, false)]  // buffer above ceiling
    [InlineData(4096, 6, 20, false)]    // buffer off-step
    [InlineData(4096, 20, 0, false)]
    [InlineData(4096, 20, 241, false)]
    public void IsValid_EnforcesRangeAndStep(int block, int buffer, int averagingPeriod, bool expected)
        => Assert.Equal(expected, new SamplingSettings(block, buffer, averagingPeriod).IsValid);

    [Fact]
    public void Default_IsValid()
        => Assert.True(SamplingSettings.Default.IsValid);

    [Theory]
    [InlineData(257, 256)]       // snap to nearest step (down)
    [InlineData(384, 512)]       // midpoint snaps away from zero (up)
    [InlineData(0, 256)]         // clamp to floor
    [InlineData(-5, 256)]        // negatives clamp to floor
    [InlineData(100000, 16384)]  // clamp to ceiling
    [InlineData(4096, 4096)]     // already valid
    public void NormalizeAnalysisBlockSize_SnapsAndClamps(int input, int expected)
        => Assert.Equal(expected, SamplingSettings.NormalizeAnalysisBlockSize(input));

    [Theory]
    [InlineData(6, 5)]      // snap down
    [InlineData(8, 10)]     // snap up
    [InlineData(0, 5)]      // clamp to floor
    [InlineData(500, 200)]  // clamp to ceiling
    [InlineData(20, 20)]    // already valid
    public void NormalizeCaptureBufferMs_SnapsAndClamps(int input, int expected)
        => Assert.Equal(expected, SamplingSettings.NormalizeCaptureBufferMs(input));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(17, 17)]
    [InlineData(500, 240)]
    public void NormalizeAveragingPeriod_SnapsAndClamps(int input, int expected)
        => Assert.Equal(expected, SamplingSettings.NormalizeAveragingPeriod(input));

    [Theory]
    [InlineData(257.4, 256)]   // decimal input rounds then snaps
    [InlineData(8191.6, 8192)]
    public void NormalizeAnalysisBlockSize_FromDecimal_RoundsThenSnaps(double input, int expected)
        => Assert.Equal(expected, SamplingSettings.NormalizeAnalysisBlockSize((decimal)input));

    [Theory]
    [InlineData(17.4, 17)]
    [InlineData(17.5, 18)]
    public void NormalizeAveragingPeriod_FromDecimal_RoundsThenSnaps(double input, int expected)
        => Assert.Equal(expected, SamplingSettings.NormalizeAveragingPeriod((decimal)input));
}
