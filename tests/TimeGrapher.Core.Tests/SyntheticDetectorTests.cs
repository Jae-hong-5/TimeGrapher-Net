using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class SyntheticDetectorTests
{
    public static TheoryData<int, int, double, double> GeneratedCases => new()
    {
        { 18000, 48000, 0.40, 0.00 },
        { 21600, 48000, 0.18, 0.02 },
        { 28800, 96000, 0.40, 0.00 },
        { 32400, 48000, 0.35, 0.00 },
        { 36000, 48000, 0.35, 0.01 },
        { 43200, 192000, 0.35, 0.00 },
    };

    [Theory]
    [MemberData(nameof(GeneratedCases))]
    public void SyntheticStreamPipelineDetectsConfiguredBphAndMetrics(int expectedBph, int sampleRate, double pcmPeak, double noisePeak)
    {
        WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = (uint)sampleRate;
        synthConfig.Bph = expectedBph;
        synthConfig.NoisePeakSignalLevel = noisePeak;
        synthConfig.PcmPeakSignalLevel = pcmPeak;

        var synth = new WatchSynthStream(synthConfig);
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: sampleRate,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0));

        float[] block = new float[4096];
        DetectorMetricsBlockUpdate update = engine.Flush();
        string resultsText = "";

        int remaining = sampleRate * 10;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            update = engine.Process(span);
            foreach (DetectedEventUpdate eventUpdate in update.MetricsEvents)
            {
                if (eventUpdate.MetricsUpdate.ResultsUpdated)
                {
                    resultsText = eventUpdate.MetricsUpdate.ResultsText;
                }
            }
            remaining -= slice;
        }

        update = engine.Flush();
        foreach (DetectedEventUpdate eventUpdate in update.MetricsEvents)
        {
            if (eventUpdate.MetricsUpdate.ResultsUpdated)
            {
                resultsText = eventUpdate.MetricsUpdate.ResultsText;
            }
        }

        Assert.Equal(TimeGrapher.Core.Detection.TgSyncStatus.Synced, update.Result.SyncStatus);
        Assert.Equal(expectedBph, update.Result.DetectedBph);
        // The readout must carry real metrics, not just be non-empty: the locked BPH
        // appears in the BPH field and a finite amplitude is reported (not the dash
        // placeholder) for the clean synced stream.
        Assert.False(string.IsNullOrWhiteSpace(resultsText));
        Assert.Contains(expectedBph.ToString(System.Globalization.CultureInfo.InvariantCulture), resultsText);
        Assert.DoesNotContain("Amplitude ---", resultsText);
    }

    [Theory]
    [InlineData(18000, 48000)]
    [InlineData(21600, 48000)]
    [InlineData(28800, 96000)]
    public void SyntheticCleanStream_AmplitudeRecoversConfiguredWithinOneDegree(int bph, int sampleRate)
    {
        // Realistic OFF (Clean), no added noise: the detected amplitude must recover the
        // configured WatchAmplitudeDegrees within the 1 deg acceptance criterion. This is
        // the end-to-end guard for the half-lift amplitude formula (WatchMetrics.Amplitude)
        // together with the engine's default A-onset latency compensation; before both, a
        // configured 270 deg read ~273 deg here.
        WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = (uint)sampleRate;
        synthConfig.Bph = bph;
        synthConfig.NoisePeakSignalLevel = 0.0;
        double configuredAmplitude = synthConfig.WatchAmplitudeDegrees;

        var synth = new WatchSynthStream(synthConfig);
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: sampleRate,
            LiftAngle: synthConfig.LiftAngleDegrees,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0));

        float[] block = new float[4096];
        double lastAmplitude = double.NaN;
        int remaining = sampleRate * 10;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            DetectorMetricsBlockUpdate update = engine.Process(span);
            foreach (DetectedEventUpdate eventUpdate in update.MetricsEvents)
            {
                if (eventUpdate.MetricsUpdate.AmplitudeSampleUpdated &&
                    eventUpdate.MetricsUpdate.AmplitudeSample.PairAverageUpdated)
                {
                    lastAmplitude = eventUpdate.MetricsUpdate.AmplitudeSample.PairAverageDeg;
                }
            }
            remaining -= slice;
        }

        Assert.False(double.IsNaN(lastAmplitude));
        Assert.True(
            Math.Abs(lastAmplitude - configuredAmplitude) <= 1.0,
            $"amplitude {lastAmplitude:F2} deg deviates from configured {configuredAmplitude} deg by more than 1 deg");
    }
}
