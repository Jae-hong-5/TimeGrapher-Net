using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// F15c stop-preemption contract: a natural-completion drain (CompleteInput) must
/// not trap a subsequent explicit TryStop. Before the fix the thread loop checked
/// completion before the stop flag and the drain ignored _stopRequested, so an
/// explicit stop issued during a long drain could not abort it. The loop now checks
/// the stop flag first and the completion drain is interruptible.
/// </summary>
public sealed class AnalysisWorkerStopPreemptsCompletionTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void ExplicitStopPreemptsAnInProgressCompletionDrain()
    {
        var buffer = new MasterAudioBuffer(SampleRate);
        using var worker = new AnalysisWorker(buffer, new AnalysisWorker.Config
        {
            SampleRate = SampleRate,
            AveragingPeriod = 2,
            SoundImageWidth = 8,
            SoundImageHeight = 8,
            ScopeSnapshotPointBudget = 256,
        });

        // Fill a large buffer so a full, non-interruptible drain would take a long
        // time relative to the stop timeout below.
        WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = SampleRate;
        synthConfig.Bph = 21600;
        synthConfig.PcmPeakSignalLevel = 0.40;
        var synth = new WatchSynthStream(synthConfig);

        var block = new float[4096];
        int remaining = SampleRate * 60; // ~60 s of audio buffered before any analysis
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            buffer.WriteSamples(span);
            remaining -= slice;
        }

        // Deterministically gate the drain at its first block so it provably cannot
        // finish (or be skipped by a stop-first race) before the explicit stop is issued.
        // Counting the block iterations proves preemption without any wall-clock guess:
        // an interruptible drain aborts after a handful of blocks once it observes the
        // stop flag, while a non-interruptible one would grind through all ~700.
        using var drainEntered = new ManualResetEventSlim(false);
        using var releaseDrain = new ManualResetEventSlim(false);
        int iterations = 0;
        worker.CompletionDrainIterationHook = () =>
        {
            if (Interlocked.Increment(ref iterations) == 1)
            {
                drainEntered.Set();
                releaseDrain.Wait(TimeSpan.FromSeconds(10));
            }
        };

        worker.Start();

        // Request natural completion: this begins the drain on the worker thread, which
        // blocks at the gate above on its first block.
        var completionThread = new Thread(() => worker.CompleteInput(TimeSpan.FromSeconds(30)))
        {
            IsBackground = true,
        };
        completionThread.Start();

        // Barrier: the drain is genuinely in progress (entered its first block iteration).
        Assert.True(drainEntered.Wait(TimeSpan.FromSeconds(10)), "completion drain was not entered");

        // Issue the explicit stop while the drain is held, then release it.
        bool stopped = false;
        var stopThread = new Thread(() => stopped = worker.TryStop(TimeSpan.FromSeconds(10)))
        {
            IsBackground = true,
        };
        stopThread.Start();
        releaseDrain.Set();

        Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)), "TryStop did not return");
        Assert.True(stopped, "explicit stop did not preempt the completion drain");
        Assert.True(completionThread.Join(TimeSpan.FromSeconds(10)), "CompleteInput did not return");

        // The interruptible drain must abort almost immediately once the stop flag is
        // observed - far fewer than the hundreds of blocks a full ~60 s drain would touch.
        Assert.True(iterations < 50, $"expected the stop to preempt the drain, but it processed {iterations} block iterations");
    }
}
