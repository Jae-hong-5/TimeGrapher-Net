using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The pure feature extractor: timing/peak statistics over a rolling A-event
/// window plus the detector's instantaneous levels. Feature math is checked
/// against closed-form hand computations.
/// </summary>
public sealed class SignalQualityFeatureExtractorTests
{
    private static TgEvent A(ulong sample, float peak) => new TgEvent
    {
        Type = TgEventType.A,
        SampleIndex = sample,
        SubSampleOffset = 0.0,
        PeakValue = peak,
    };

    private static TgEvent[] ConstantInterval(int eventCount, ulong interval, float peak)
    {
        var events = new TgEvent[eventCount];
        ulong sample = 0;
        for (int i = 0; i < eventCount; i++)
        {
            events[i] = A(sample, peak);
            sample += interval;
        }

        return events;
    }

    [Fact]
    public void ReportsNoFeaturesUntilEnoughIntervalsAccumulate()
    {
        var extractor = new SignalQualityFeatureExtractor();

        // 4 A events => only 3 intervals => still below the minimum of 4.
        extractor.Observe(true, 1.0f, 0.01f, 0.2f, 0, 0, ConstantInterval(4, 6000, 1.0f));
        Assert.False(extractor.TryGetFeatures(out _));

        // One more A event => 4 intervals => features available.
        extractor.Observe(true, 1.0f, 0.01f, 0.2f, 0, 0, new[] { A(24000, 1.0f), A(30000, 1.0f) });
        Assert.True(extractor.TryGetFeatures(out _));
    }

    [Fact]
    public void CleanConstantSignalYieldsZeroJitterAndExpectedLevels()
    {
        var extractor = new SignalQualityFeatureExtractor();

        // referencePeak 1.0 / noiseFloor 0.01 => 20*log10(100) = 40 dB.
        // referencePeak 1.0 / minPeakThreshold 0.2 => margin 5.
        extractor.Observe(true, 1.0f, 0.01f, 0.2f, 0, 0, ConstantInterval(12, 6000, 1.0f));

        Assert.True(extractor.TryGetFeatures(out SignalQualityFeatures f));
        Assert.Equal(0.0f, f.IntervalJitterCv, 5);
        Assert.Equal(0.0f, f.PeakLevelCv, 5);
        Assert.Equal(40.0f, f.SnrDb, 3);
        Assert.Equal(5.0f, f.PeakMarginRatio, 4);
        Assert.Equal(0.01f, f.NoiseFloorLevel, 6);
        Assert.Equal(0.0f, f.MissedBeatRate);
        Assert.Equal(0.0f, f.SyncLossRate);
        Assert.Equal(1.0f, f.SyncedFraction);
    }

    [Fact]
    public void AlternatingIntervalsRaiseTheJitterCoefficient()
    {
        var extractor = new SignalQualityFeatureExtractor();

        // Intervals alternate 6000 / 3000: mean 4500, population sigma 1500,
        // CV = 1500 / 4500 = 1/3.
        var events = new System.Collections.Generic.List<TgEvent>();
        ulong sample = 0;
        events.Add(A(sample, 1.0f));
        for (int i = 0; i < 12; i++)
        {
            sample += (i % 2 == 0) ? 6000UL : 3000UL;
            events.Add(A(sample, 1.0f));
        }

        extractor.Observe(true, 1.0f, 0.01f, 0.2f, 0, 0, events.ToArray());

        Assert.True(extractor.TryGetFeatures(out SignalQualityFeatures f));
        Assert.Equal(1.0f / 3.0f, f.IntervalJitterCv, 3);
    }

    [Fact]
    public void UnsyncedObservationsDriveSyncedFractionToZero()
    {
        var extractor = new SignalQualityFeatureExtractor();

        extractor.Observe(false, 1.0f, 0.01f, 0.2f, 0, 0, ConstantInterval(8, 6000, 1.0f));

        Assert.True(extractor.TryGetFeatures(out SignalQualityFeatures f));
        Assert.Equal(0.0f, f.SyncedFraction);
    }

    [Fact]
    public void AccumulatedMissedBeatsAcrossTheWindowRaiseTheRate()
    {
        var extractor = new SignalQualityFeatureExtractor();

        // First A only seeds lastA. The 5 recorded intervals then carry the
        // cumulative missed counter {0,1,2,3,4}: delta 4 over 5 intervals = 0.8.
        ulong sample = 0;
        extractor.Observe(true, 1.0f, 0.01f, 0.2f, 0, 0, new[] { A(sample, 1.0f) });
        foreach (ulong m in new ulong[] { 0, 1, 2, 3, 4 })
        {
            sample += 6000;
            extractor.Observe(true, 1.0f, 0.01f, 0.2f, m, 0, new[] { A(sample, 1.0f) });
        }

        Assert.True(extractor.TryGetFeatures(out SignalQualityFeatures f));
        Assert.Equal(0.8f, f.MissedBeatRate, 5);
    }

    [Fact]
    public void ResetClearsRollingState()
    {
        var extractor = new SignalQualityFeatureExtractor();
        extractor.Observe(true, 1.0f, 0.01f, 0.2f, 0, 0, ConstantInterval(12, 6000, 1.0f));
        Assert.True(extractor.TryGetFeatures(out _));

        extractor.Reset();
        Assert.False(extractor.TryGetFeatures(out _));
    }
}
