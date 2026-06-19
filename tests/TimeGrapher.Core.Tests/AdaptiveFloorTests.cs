using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// I-1 AdaptiveFloor mechanism tests, driving TgDetectorCore directly with a
/// hand-built envelope (fluctuating noise carpet + rectangular bursts) so the
/// threshold arithmetic is exactly controlled.
///
/// Noise carpet: 7-sample pattern of 2x 0.0008 + 5x 0.00112, chosen so the
/// snap-down min tracker (NoiseFloor) sits at ~0.0008 while the decimated
/// 75th-percentile silence estimate sits at 0.00112, reproducing the
/// realistic split between the two noise statistics. Period 7 is coprime to
/// the 48-sample decimation stride so the percentile ring sees both values.
/// Pre-adaptation minPeakThr = 0.00112 + 0.2*(10*0.0008 - 0.00112) = ~0.0025.
/// </summary>
public sealed class AdaptiveFloorTests
{
    private const double Fs = 48000.0;
    private const int BurstWidth = 576;          // 12 ms
    private const int BurstSpacing = 12000;      // 250 ms
    private const float NoiseLow = 0.0008f;
    private const float NoiseHigh = 0.00112f;

    private static TgDetectorCore NewCore()
    {
        var core = new TgDetectorCore();
        core.Init(Fs);
        // Test-friendly parameters: a wider engagement window than the
        // defaults so the mechanism (not parameter tuning) is under test.
        core.RejectedPeakMinSnr = 1.5;
        core.AdaptiveFloorMinMul = 2.0;
        core.RefDecayAfterS = 1.0;
        core.RefDecayTauS = 1.0;
        return core;
    }

    private static float[] BuildEnvelope(int totalSamples, IEnumerable<(int Start, float SignalLevel)> bursts)
    {
        var env = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
        {
            env[i] = (i % 7) < 2 ? NoiseLow : NoiseHigh;
        }
        foreach ((int start, float signalLevel) in bursts)
        {
            for (int i = start; i < start + BurstWidth && i < totalSamples; i++)
            {
                env[i] = signalLevel;
            }
        }
        return env;
    }

    private static List<TgRawEvent> Run(TgDetectorCore core, float[] envelope)
    {
        var events = new TgRawEvent[512];
        int count = 0;
        for (int offset = 0; offset < envelope.Length; offset += 4096)
        {
            int n = Math.Min(4096, envelope.Length - offset);
            core.Process(envelope.AsSpan(offset, n), n, events, ref count, events.Length);
        }
        return events.Take(count).ToList();
    }

    private static IEnumerable<(int Start, float SignalLevel)> BurstTrain(double fromS, int count, float signalLevel)
    {
        for (int k = 0; k < count; k++)
        {
            yield return ((int)((fromS + 0.25 * k) * Fs), signalLevel);
        }
    }

    [Fact]
    public void WeakBursts_AdaptiveFloorRecoversAfterRejectedHistory()
    {
        // 0.0024 bursts sit just under the pre-adaptation min-peak threshold
        // (~0.0025): the W-2 floor rejects every one, and rejected bursts
        // leave no trace, so the lockout would be permanent without the
        // rejected-peak shadow history.
        float[] envelope = BuildEnvelope((int)(6.5 * Fs), BurstTrain(1.0, 20, 0.0024f));

        List<TgRawEvent> adaptive = Run(NewCore(), envelope);

        Assert.NotEmpty(adaptive);
        // Adaptation requires RejectedPeakMinCount (8) rejected bursts first:
        // the first acceptance cannot come before burst #9.
        ulong burst9Start = (ulong)((1.0 + 8 * 0.25) * Fs);
        Assert.True(adaptive[0].SampleIndex >= burst9Start - BurstWidth,
            $"first accept at {adaptive[0].SampleIndex}, before the 8-rejection minimum {burst9Start}");
        // Once adapted, the remaining train is detected (A+C per burst).
        Assert.True(adaptive.Count >= 8, $"only {adaptive.Count} events after adaptation");
    }

    [Fact]
    public void LoudToQuietTransition_AdaptiveFloorDecaysAndReacquires()
    {
        // 10 loud bursts latch the reference peak at 0.05; quiet 0.0024
        // bursts follow. The reference decays after RefDecayAfterS of no
        // accepts and the rejected-peak floor takes over, so detection resumes.
        var bursts = new List<(int, float)>();
        bursts.AddRange(BurstTrain(1.0, 10, 0.05f));
        bursts.AddRange(BurstTrain(3.5, 30, 0.0024f));
        float[] envelope = BuildEnvelope((int)(11.5 * Fs), bursts);

        List<TgRawEvent> adaptive = Run(NewCore(), envelope);

        ulong quietStart = (ulong)(3.5 * Fs);
        List<TgRawEvent> quietAccepts = adaptive.Where(ev => ev.SampleIndex >= quietStart).ToList();
        Assert.NotEmpty(quietAccepts);
        // Reacquisition happens within the decay horizon (well before the
        // end of the quiet train), not just at the very last burst.
        ulong deadline = (ulong)(9.0 * Fs);
        Assert.True(quietAccepts[0].SampleIndex <= deadline,
            $"first quiet accept at {quietAccepts[0].SampleIndex}, after deadline {deadline}");
    }

    [Fact]
    public void LoudImpulseAfterDetectionGap_DoesNotWipeTheMedianHistory()
    {
        // A single 0.12 impulse lands after a 3 s detection gap. The history
        // restart must NOT fire for it (the impulse is above the stale
        // median, so this is not a downward regime move): the 16-entry
        // median absorbs the outlier and the 0.01 ticks that resume right
        // after stay accepted. Without the below-median guard the restart
        // latched the max-of-few reference at the impulse height and
        // blacked out the resumed ticks for seconds.
        var bursts = new List<(int, float)>();
        bursts.AddRange(BurstTrain(1.0, 12, 0.01f));   // healthy train to 3.75 s
        bursts.Add(((int)(6.8 * Fs), 0.12f));          // impulse after a ~3 s gap
        bursts.AddRange(BurstTrain(7.1, 8, 0.01f));    // ticks resume
        float[] envelope = BuildEnvelope((int)(9.5 * Fs), bursts);

        List<TgRawEvent> adaptive = Run(NewCore(), envelope);

        ulong resumeStart = (ulong)(7.1 * Fs);
        int resumedAccepts = adaptive.Count(ev => ev.SampleIndex >= resumeStart);
        // 8 resumed bursts emit A+C each when accepted; require most of them.
        Assert.True(resumedAccepts >= 12,
            $"only {resumedAccepts} events after the impulse; the median history was wiped");
    }

    [Fact]
    public void NearNoiseBumps_DoNotFeedTheShadowRing_NoAdaptation()
    {
        // 0.0015 bursts are below RejectedPeakMinSnr * effNoise (~0.00168):
        // they must never qualify for the shadow ring, so the floor never
        // adapts and the detector stays silent even with the floor always on.
        float[] envelope = BuildEnvelope((int)(6.5 * Fs), BurstTrain(1.0, 20, 0.0015f));

        List<TgRawEvent> adaptive = Run(NewCore(), envelope);

        Assert.Empty(adaptive);
    }
}
