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
        synthConfig.NoisePeakAmplitude = noisePeak;
        synthConfig.PcmPeakAmplitude = pcmPeak;

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
        Assert.False(string.IsNullOrWhiteSpace(resultsText));
    }

    [Fact]
    public void LockAcquiringBatchEmitsNoEventsAndLaterBatchesDo()
    {
        // The batch that acquires lock holds only the pre-lock bootstrap events.
        // With SuppressPreSyncEvents (always on in the engine) those must be
        // suppressed so they never seed metrics before the first locked beat,
        // while subsequent locked batches must emit events normally.
        WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = 48000;
        synthConfig.Bph = 21600;
        synthConfig.PcmPeakAmplitude = 0.40;

        var synth = new WatchSynthStream(synthConfig);
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: 48000,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0));

        float[] block = new float[4096];
        int remaining = 48000 * 12;
        bool sawAcquiringBatch = false;
        bool postLockEmitted = false;

        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            synth.Generate(block.AsSpan(0, slice));
            DetectorMetricsBlockUpdate update = engine.Process(block.AsSpan(0, slice));

            if (update.Result.SyncAcquiredEvent)
            {
                Assert.Empty(update.MetricsEvents);
                sawAcquiringBatch = true;
            }
            else if (sawAcquiringBatch &&
                     update.Result.SyncStatus == TimeGrapher.Core.Detection.TgSyncStatus.Synced &&
                     update.MetricsEvents.Count > 0)
            {
                postLockEmitted = true;
            }

            remaining -= slice;
        }

        Assert.True(sawAcquiringBatch, "expected the detector to acquire lock");
        Assert.True(postLockEmitted, "expected post-lock batches to emit events");
    }
}
