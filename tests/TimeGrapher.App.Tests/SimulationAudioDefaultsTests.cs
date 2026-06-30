using TimeGrapher.App.Audio;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SimulationAudioDefaultsTests
{
    public static IEnumerable<object[]> StandardSampleRates =>
        AudioSampleRates.Standard.Select(rate => new object[] { rate });

    [Theory]
    [MemberData(nameof(StandardSampleRates))]
    public void DefaultSimulationDoesNotEmitClippingWarningAtSelectableRates(int sampleRate)
    {
        var buffer = new MasterAudioBuffer(sampleRate);
        using var worker = new AnalysisWorker(buffer, NewWorkerConfig(sampleRate));
        AnalysisFrame? capturedFrame = null;
        bool clippingEverReported = false;
        worker.AnalysisFrameReady += frame =>
        {
            capturedFrame = frame;

            // Inspect EVERY frame, not just the last: BeatSegments is a bounded recent ring,
            // so an early transient clipping warning could rotate out before the final frame.
            BeatSegmentsSnapshot? segments = frame.BeatSegments;
            if (segments == null)
            {
                return;
            }

            if ((segments.Quality & SignalQualityFlags.ClippedSignal) != 0)
            {
                clippingEverReported = true;
            }

            foreach (var segment in segments.Segments)
            {
                if ((segment.Quality & SignalQualityFlags.ClippedSignal) != 0)
                {
                    clippingEverReported = true;
                }
            }
        };

        var cfg = WatchSynthStreamConfig.Realistic();
        LeftPanelSettings defaults = LeftPanelSettings.Default;
        cfg.SampleRateHz = (uint)sampleRate;
        cfg.Bph = defaults.SimulationBph;
        cfg.BeatErrorMs = -defaults.SimulationBeatError;
        cfg.PcmPeakSignalLevel = SimulationAudioDefaults.PcmPeakSignalLevel;
        cfg.WatchAmplitudeDegrees = defaults.SimulationAmplitude;
        cfg.LiftAngleDegrees = defaults.LiftAngle;
        cfg.RateErrorSPerDay = defaults.SimulationErrorRate;
        cfg.AClusterLevelScale = defaults.SimulationSignalAScale;
        cfg.BClusterLevelScale = defaults.SimulationSignalBScale;
        cfg.CClusterLevelScale = defaults.SimulationSignalCScale;

        var synth = new WatchSynthStream(cfg);
        var block = new float[4096];
        int remaining = sampleRate * 6;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            buffer.WriteSamples(span);
            worker.HandleInputData();
            remaining -= slice;
        }

        AnalysisFrame frame = Assert.IsType<AnalysisFrame>(capturedFrame);
        Assert.NotNull(frame.BeatSegments);
        BeatSegmentsSnapshot snapshot = frame.BeatSegments;
        Assert.NotEmpty(snapshot.Segments);
        Assert.Equal(SignalQualityFlags.None, snapshot.Quality & SignalQualityFlags.ClippedSignal);
        Assert.DoesNotContain(snapshot.Segments, segment =>
            (segment.Quality & SignalQualityFlags.ClippedSignal) != 0);

        // No frame across the whole run should have reported a clipping warning.
        Assert.False(clippingEverReported, "a clipping warning was reported during the default simulation");
    }

    private static AnalysisWorker.Config NewWorkerConfig(int sampleRate)
    {
        var settings = new AnalysisRunSettings(
            SampleRate: sampleRate,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 200.0,
            SoundImageWidth: 100,
            SoundImageHeight: 100,
            ScopeSnapshotPointBudget: 8000,
            AnalysisBlockSize: 4096);

        return settings.ToWorkerConfig(sessionId: 1, sampleWriter: null);
    }
}
