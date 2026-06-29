using System.Diagnostics;
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

        worker.Start();

        // Request natural completion (begins the drain on the worker thread) and then
        // an explicit stop. The stop must preempt the drain and the worker must join
        // well within the timeout instead of finishing the whole 60 s buffer.
        var completionThread = new Thread(() => worker.CompleteInput(TimeSpan.FromSeconds(30)))
        {
            IsBackground = true,
        };
        completionThread.Start();

        var sw = Stopwatch.StartNew();
        bool stopped = worker.TryStop(TimeSpan.FromSeconds(10));
        sw.Stop();

        Assert.True(stopped, "explicit stop did not preempt the completion drain");
        completionThread.Join(TimeSpan.FromSeconds(10));
    }
}
