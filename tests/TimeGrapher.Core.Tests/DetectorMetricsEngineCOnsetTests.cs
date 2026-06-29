using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The C-onset timing selection in DetectorMetricsEngine.EventSample. The detector
/// always timestamps a C event's primary SampleIndex at the cluster PEAK (CPlacement
/// stays at the Peak default) but also carries the cluster's rising-edge ONSET metadata.
/// With UseCOnset=true the engine must drive metrics off the earlier onset, not the peak.
/// </summary>
public sealed class DetectorMetricsEngineCOnsetTests
{
    private const int Bph = 28800;
    private const int SampleRate = 48000;

    private static WatchSynthStream CleanSynth()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = SampleRate;
        cfg.Bph = Bph;
        cfg.NoisePeakSignalLevel = 0.0;
        cfg.PcmPeakSignalLevel = 0.40;
        return new WatchSynthStream(cfg);
    }

    private static DetectorMetricsEngineConfig Config(bool useCOnset) => new(
        SampleRate: SampleRate,
        LiftAngle: 52.0,
        AveragingPeriod: 2,
        UseCOnset: useCOnset,
        AutoBph: true,
        ManualBph: 0,
        HpfCutoffHz: 0.0);

    private static List<DetectedEventUpdate> RunCEvents(bool useCOnset, int seconds)
    {
        var engine = new DetectorMetricsEngine(Config(useCOnset));
        WatchSynthStream synth = CleanSynth();
        var cEvents = new List<DetectedEventUpdate>();

        float[] block = new float[4096];
        int remaining = SampleRate * seconds;
        engine.Flush();
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            DetectorMetricsBlockUpdate update = engine.Process(span);
            foreach (DetectedEventUpdate ev in update.MetricsEvents)
            {
                if (ev.Event.Type == TgEventType.C)
                {
                    cEvents.Add(ev);
                }
            }

            remaining -= slice;
        }

        return cEvents;
    }

    [Fact]
    public void UseCOnset_DrivesMetricsOffTheOnsetTimingNotThePeak()
    {
        List<DetectedEventUpdate> cEvents = RunCEvents(useCOnset: true, seconds: 10);

        // The clean stream must yield onset-located C events; otherwise this asserts nothing.
        var onsetValid = cEvents.Where(ev => ev.Event.OnsetValid).ToList();
        Assert.NotEmpty(onsetValid);

        foreach (DetectedEventUpdate ev in onsetValid)
        {
            double onset = ev.Event.OnsetSampleIndex + ev.Event.OnsetSubSampleOffset;
            double peak = ev.Event.SampleIndex + ev.Event.SubSampleOffset;

            // With UseCOnset=true the engine's chosen event sample is the onset.
            Assert.Equal(onset, ev.EventSample, precision: 9);
        }

        // At least one onset-located C event must have its onset strictly before its peak,
        // so the assertion above genuinely distinguishes onset from peak timing.
        Assert.Contains(
            onsetValid,
            ev => ev.Event.OnsetSampleIndex + ev.Event.OnsetSubSampleOffset
                < ev.Event.SampleIndex + ev.Event.SubSampleOffset);
    }

    [Fact]
    public void WithoutCOnset_DrivesMetricsOffThePeakTiming()
    {
        List<DetectedEventUpdate> cEvents = RunCEvents(useCOnset: false, seconds: 10);

        Assert.NotEmpty(cEvents);
        foreach (DetectedEventUpdate ev in cEvents)
        {
            double peak = ev.Event.SampleIndex + ev.Event.SubSampleOffset;

            // UseCOnset=false always uses the peak SampleIndex, even when onset metadata exists.
            Assert.Equal(peak, ev.EventSample, precision: 9);
        }
    }
}
