using TimeGrapher.App.Services;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AnalysisFrameRenderSchedulerTests
{
    [Fact]
    public void EnqueueRendersFramePostedToUi()
    {
        var harness = new SchedulerHarness();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 10 });
        harness.RunNextPostedAction();

        Assert.Collection(
            harness.Rendered,
            rendered =>
            {
                Assert.Equal<ulong>(10, rendered.Frame.SourceId);
                Assert.Equal<ulong>(0, rendered.DroppedFrames);
            });
    }

    [Fact]
    public void EnqueueCoalescesPendingFramesAndReportsDrops()
    {
        var harness = new SchedulerHarness();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 1 });
        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2 });
        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 3 });
        harness.RunNextPostedAction();

        Assert.Collection(
            harness.Rendered,
            rendered =>
            {
                Assert.Equal<ulong>(3, rendered.Frame.SourceId);
                Assert.Equal<ulong>(2, rendered.DroppedFrames);
            });
    }

    [Fact]
    public void EnqueueMergesTransientSignalsFromDisplacedFrames()
    {
        var harness = new SchedulerHarness();
        var soundImage = new PixelBuffer(4, 4);

        harness.Scheduler.Enqueue(new AnalysisFrame
        {
            SourceId = 1,
            InputOverrun = true,
            InputSamplesDropped = 100,
            SoundImage = soundImage,
            SoundImageUpdated = true,
        });
        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2, InputSamplesDropped = 5 });
        harness.RunNextPostedAction();

        Assert.Collection(
            harness.Rendered,
            rendered =>
            {
                Assert.Equal<ulong>(2, rendered.Frame.SourceId);
                Assert.True(rendered.Frame.InputOverrun);
                Assert.Equal<ulong>(105, rendered.Frame.InputSamplesDropped);
                Assert.True(rendered.Frame.SoundImageUpdated);
                Assert.Same(soundImage, rendered.Frame.SoundImage);
            });
    }

    [Fact]
    public void EnqueueMergesSpectrogramImageFromDisplacedFrames()
    {
        var harness = new SchedulerHarness();
        var spectrogramImage = new PixelBuffer(4, 4);

        harness.Scheduler.Enqueue(new AnalysisFrame
        {
            SourceId = 1,
            SpectrogramImage = spectrogramImage,
            SpectrogramImageUpdated = true,
            SpectrogramLiveColumn = 42,
            SpectrogramColumnSeconds = 0.0106,
            SpectrogramBeatPeriodS = 0.125,
        });
        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2 });
        harness.RunNextPostedAction();

        Assert.Collection(
            harness.Rendered,
            rendered =>
            {
                Assert.Equal<ulong>(2, rendered.Frame.SourceId);
                Assert.True(rendered.Frame.SpectrogramImageUpdated);
                Assert.Same(spectrogramImage, rendered.Frame.SpectrogramImage);
                // The windowing metadata must travel with the image or the
                // consumer skips the crop (choppy spectrogram under load).
                Assert.Equal(42, rendered.Frame.SpectrogramLiveColumn);
                Assert.Equal(0.0106, rendered.Frame.SpectrogramColumnSeconds);
                Assert.Equal(0.125, rendered.Frame.SpectrogramBeatPeriodS);
            });
    }

    [Fact]
    public void EnqueueMergesCaptureLowerBoundFlagFromDisplacedFrames()
    {
        var harness = new SchedulerHarness();

        // A one-off global stall flags exactly one frame; the unflagged
        // follow-up pass must not displace the honesty marker.
        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 1, CaptureTimestampIsLowerBound = true });
        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2 });
        harness.RunNextPostedAction();

        Assert.Collection(
            harness.Rendered,
            rendered =>
            {
                Assert.Equal<ulong>(2, rendered.Frame.SourceId);
                Assert.True(rendered.Frame.CaptureTimestampIsLowerBound);
            });
    }

    [Fact]
    public void EnqueueKeepsReplacementSoundImageWhenBothFramesUpdated()
    {
        var harness = new SchedulerHarness();
        var newerImage = new PixelBuffer(4, 4);

        harness.Scheduler.Enqueue(new AnalysisFrame
        {
            SourceId = 1,
            SoundImage = new PixelBuffer(4, 4),
            SoundImageUpdated = true,
        });
        harness.Scheduler.Enqueue(new AnalysisFrame
        {
            SourceId = 2,
            SoundImage = newerImage,
            SoundImageUpdated = true,
        });
        harness.RunNextPostedAction();

        Assert.Same(newerImage, harness.Rendered.Single().Frame.SoundImage);
    }

    [Fact]
    public void ResetInvalidatesQueuedRender()
    {
        var harness = new SchedulerHarness();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 1 });
        harness.Scheduler.Reset();
        harness.RunNextPostedAction();

        Assert.Empty(harness.Rendered);
    }

    [Fact]
    public void ResetTimingPreservesPendingFrame()
    {
        var harness = new SchedulerHarness();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 1 });
        harness.RunNextPostedAction();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2 });
        harness.Scheduler.ResetTiming();
        harness.RunNextPostedAction();

        Assert.Equal(new ulong[] { 1, 2 }, harness.Rendered.Select(rendered => rendered.Frame.SourceId));
    }

    [Fact]
    public void ResetTimingDrainsFrameWaitingOnRefreshDelay()
    {
        var harness = new SchedulerHarness { HoldDelays = true };

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 1 });
        harness.RunNextPostedAction(); // renders frame 1, arms the refresh deadline

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2 });
        harness.RunNextPostedAction(); // frame 2 is throttled behind an in-flight
                                       // (never-completing) delay; nothing posted

        Assert.Single(harness.Delays);
        Assert.Single(harness.Rendered); // frame 2 not rendered yet

        // ResetTiming must surface the queued frame immediately (tab switch), not
        // wait out the delay: it posts a drain that renders frame 2 now.
        harness.Scheduler.ResetTiming();
        harness.RunNextPostedAction();

        Assert.Equal(new ulong[] { 1, 2 }, harness.Rendered.Select(rendered => rendered.Frame.SourceId));
    }

    [Fact]
    public void RefreshIntervalDelaysNextRender()
    {
        var harness = new SchedulerHarness();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 1 });
        harness.RunNextPostedAction();

        harness.Scheduler.Enqueue(new AnalysisFrame { SourceId = 2 });
        harness.RunNextPostedAction();

        Assert.Single(harness.Delays);
        Assert.Single(harness.Rendered);

        harness.UtcNow = harness.UtcNow.Add(harness.Delays[0]);
        harness.RunNextPostedAction();

        Assert.Equal(new ulong[] { 1, 2 }, harness.Rendered.Select(rendered => rendered.Frame.SourceId));
    }

    private sealed class SchedulerHarness
    {
        private readonly Queue<Action> _postedActions = new();

        public SchedulerHarness()
        {
            Scheduler = new AnalysisFrameRenderScheduler(
                action => _postedActions.Enqueue(action),
                () => 100,
                (frame, droppedFrames) => Rendered.Add(new RenderedFrame(frame, droppedFrames)),
                () => UtcNow,
                delay =>
                {
                    Delays.Add(delay);
                    // HoldDelays leaves the throttle delay pending (as a real
                    // Task.Delay would) so a frame can sit queued behind it.
                    return HoldDelays ? new TaskCompletionSource().Task : Task.CompletedTask;
                });
        }

        public AnalysisFrameRenderScheduler Scheduler { get; }

        public bool HoldDelays { get; set; }

        public DateTime UtcNow { get; set; } = new(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc);

        public List<RenderedFrame> Rendered { get; } = new();

        public List<TimeSpan> Delays { get; } = new();

        public void RunNextPostedAction()
        {
            if (_postedActions.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }

    private readonly record struct RenderedFrame(AnalysisFrame Frame, ulong DroppedFrames);
}
