using TimeGrapher.Core.Metrics;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class WatchMetricsTests
{
    // Half-lift model: Amplitude(liftAngle, t1, BPH) = liftAngle / (2*sin(pi*t1 / (7200/BPH))).
    // 7200/BPH == 2T (T = 3600/BPH). Choosing t1 so the sine argument is a known angle
    // gives exact expected values.

    [Fact]
    public void Amplitude_WhenHalfLiftSineArgumentIsPiOverSix_EqualsLiftAngle()
    {
        // pi*t1 / (7200/BPH) = pi/6  ->  t1 = 1200/BPH; 2*sin(pi/6) = 1  ->  Amp = liftAngle.
        const double bph = 3600.0;
        double t1 = 1200.0 / bph; // 1/3 s
        Assert.Equal(52.0, WatchMetrics.Amplitude(52.0, t1, bph), 6);
    }

    [Fact]
    public void Amplitude_WhenHalfLiftSineArgumentIsHalfPi_EqualsHalfLiftAngle()
    {
        // pi*t1 / (7200/BPH) = pi/2  ->  t1 = 3600/BPH = T; 2*sin(pi/2) = 2  ->  Amp = liftAngle/2.
        // This is the physical floor: t_AC spanning a full half-oscillation means the
        // balance only just reaches +-liftAngle/2 at its turning point.
        const double bph = 3600.0;
        double t1 = 3600.0 / bph; // 1 s
        Assert.Equal(26.0, WatchMetrics.Amplitude(52.0, t1, bph), 6);
    }

    [Fact]
    public void Amplitude_IncreasesAsSwingTimeShrinks()
    {
        // Over (0, T) the half-lift sine argument (pi*t1/2T) stays in (0, pi/2) where sine
        // is increasing, so a larger t1 yields a larger sine and thus a smaller amplitude.
        // Verify the monotonic relationship for two t1 well inside that range.
        const double bph = 28800.0;
        double reference = 1800.0 / bph; // T/2, well below the full half-oscillation T
        double small = WatchMetrics.Amplitude(52.0, reference * 0.25, bph);
        double large = WatchMetrics.Amplitude(52.0, reference * 0.75, bph);
        Assert.True(small > large, $"expected amplitude to fall as t1 grows (small={small}, large={large})");
    }

    [Fact]
    public void HandleCEvent_DoesNotUpdateAmplitudeWhenAcTimingBreaksRecentPattern()
    {
        var metrics = new WatchMetrics(new WatchMetricsConfig { SampleRate = 48000, LiftAngle = 52.0 });
        const double bph = 28800.0;
        double a = 0.0;

        for (int i = 0; i < 5; i++)
        {
            metrics.HandleAEvent(a, true, bph);
            metrics.HandleCEvent(a + 480, true, bph);
            a += 6000;
        }

        metrics.HandleAEvent(a, true, bph);
        var update = metrics.HandleCEvent(a + 120, true, bph);

        Assert.False(update.AmplitudeSampleUpdated);
    }
}
