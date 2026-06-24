using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Maps detector output (PCM, threshold, A/C events) into the Rate/Scope frame contract:
/// replace-snapshot scope series bounded by the point budget, sync flag, and themed event
/// markers (A = green, C = red).
/// </summary>
public sealed class ScopeRateFrameProjectorTests
{
    private const int SampleRate = 48000;

    private static DetectorResultSnapshot Result(TgSyncStatus sync, float[] pcm, int len, float threshold) =>
        new(sync, 21600, 0.0, Array.Empty<TgEvent>(), pcm, len, 0UL,
            false, false, false, threshold, 0f, 0f, 0f);

    [Fact]
    public void Project_PublishesBoundedReplaceScopeSeriesAndSyncFlag()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 256);
        var pcm = new float[4800];
        Array.Fill(pcm, 0.1f);
        var update = new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, pcm, pcm.Length, 0.2f),
            Array.Empty<DetectedEventUpdate>());
        var frame = new AnalysisFrame();

        projector.Project(update, frame);
        projector.AppendSnapshot(frame);

        GraphSeriesFrame pcmSeries = Assert.Single(frame.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopePcm);
        GraphSeriesFrame threshold = Assert.Single(frame.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopeThreshold);

        Assert.True(frame.BeatSynced);
        Assert.True(pcmSeries.Replace);
        Assert.InRange(pcmSeries.X.Count, 1, 256);
        Assert.Equal(pcmSeries.X.Count, pcmSeries.Y.Count);
        // The threshold (trigger) line must actually be published over the same
        // window as the PCM: without these the Assert.All below is vacuous and the
        // trigger line could silently disappear while the test still passes.
        Assert.True(threshold.Replace);
        Assert.NotEmpty(threshold.Y);
        Assert.Equal(pcmSeries.Y.Count, threshold.Y.Count);
        Assert.Equal(threshold.X.Count, threshold.Y.Count);
        Assert.All(threshold.Y, y => Assert.Equal(0.2, y, 5));
    }

    [Fact]
    public void Project_MapsAEventToGreenAndCEventToRedVerticalMarker()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 256);
        var aEvent = new TgEvent { Type = TgEventType.A, PeakValue = 0.5f, SampleIndex = 1000 };
        var cEvent = new TgEvent { Type = TgEventType.C, PeakValue = 0.4f, SampleIndex = 2000 };
        var events = new List<DetectedEventUpdate>
        {
            new(aEvent, 1000.0, new WatchMetricsUpdate()),
            new(cEvent, 2000.0, new WatchMetricsUpdate()),
        };
        var update = new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, Array.Empty<float>(), 0, 0.2f),
            events,
            Array.Empty<DetectedEventUpdate>());
        var frame = new AnalysisFrame();

        projector.Project(update, frame);
        projector.AppendSnapshot(frame);

        Assert.Contains(frame.VerticalMarkers, m => m.Color == Argb.Green && m.X == 1000.0);
        Assert.Contains(frame.VerticalMarkers, m => m.Color == Argb.Red && m.X == 2000.0);
    }

    [Fact]
    public void Project_HonorsSmallScopePointBudget()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 8);
        var pcm = new float[48000];
        Array.Fill(pcm, 0.05f);
        var update = new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, pcm, pcm.Length, 0.1f),
            Array.Empty<DetectedEventUpdate>());
        var frame = new AnalysisFrame();

        projector.Project(update, frame);
        projector.AppendSnapshot(frame);

        GraphSeriesFrame pcmSeries = Assert.Single(frame.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopePcm);
        Assert.InRange(pcmSeries.X.Count, 1, 8);
    }

    [Fact]
    public void AppendSnapshot_ReusesRateSeriesSnapshotUntilNextRateUpdate()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 256);
        var metricsWithTic = new WatchMetricsUpdate();
        metricsWithTic.SetTicRate(new[] { 1.0, 2.0 }, new[] { 0.1, 0.2 });
        var aEvent = new TgEvent { Type = TgEventType.A, PeakValue = 0.5f, SampleIndex = 1000 };
        var update = new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, Array.Empty<float>(), 0, 0.2f),
            new List<DetectedEventUpdate> { new(aEvent, 1000.0, metricsWithTic) });

        var frame1 = new AnalysisFrame();
        projector.Project(update, frame1);
        projector.AppendSnapshot(frame1);

        // No new rate update between frames -> the immutable series snapshot is
        // shared, not re-copied per frame.
        var frame2 = new AnalysisFrame();
        projector.Project(new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, Array.Empty<float>(), 0, 0.2f),
            Array.Empty<DetectedEventUpdate>()), frame2);
        projector.AppendSnapshot(frame2);

        GraphSeriesFrame tic1 = Assert.Single(frame1.RateSeries, s => s.Id == AnalysisGraphSeries.RateTic);
        GraphSeriesFrame tic2 = Assert.Single(frame2.RateSeries, s => s.Id == AnalysisGraphSeries.RateTic);
        Assert.Same(tic1, tic2);

        // A new rate update must produce a fresh snapshot object.
        var frame3 = new AnalysisFrame();
        projector.Project(update, frame3);
        projector.AppendSnapshot(frame3);
        GraphSeriesFrame tic3 = Assert.Single(frame3.RateSeries, s => s.Id == AnalysisGraphSeries.RateTic);
        Assert.NotSame(tic1, tic3);
    }

    [Fact]
    public void AppendSnapshot_ThrottlesScopeSliceRebuildWithinPublishInterval()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 256);

        GraphSeriesFrame PublishBlock(int length)
        {
            var data = new float[length];
            Array.Fill(data, 0.1f);
            var frame = new AnalysisFrame();
            projector.Project(new DetectorMetricsBlockUpdate(
                Result(TgSyncStatus.Synced, data, data.Length, 0.2f),
                Array.Empty<DetectedEventUpdate>()), frame);
            projector.AppendSnapshot(frame);
            return Assert.Single(frame.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopePcm);
        }

        // First snapshot publishes; a second within the 0.05 s (2400-sample) floor
        // re-attaches the same immutable slice instead of re-copying it.
        GraphSeriesFrame first = PublishBlock(1000);
        GraphSeriesFrame second = PublishBlock(1000);
        Assert.Same(first, second);

        // Crossing the floor rebuilds a fresh slice.
        GraphSeriesFrame third = PublishBlock(3000);
        Assert.NotSame(first, third);
    }

    [Fact]
    public void AppendSnapshot_ForceRepublishesSliceWithinInterval()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 256);

        var frame1 = new AnalysisFrame();
        var block = new float[1000];
        Array.Fill(block, 0.1f);
        projector.Project(new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, block, block.Length, 0.2f),
            Array.Empty<DetectedEventUpdate>()), frame1);
        projector.AppendSnapshot(frame1);
        GraphSeriesFrame first = Assert.Single(frame1.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopePcm);

        // A tiny advance stays within the publish floor, but force (the drain/flush
        // path) rebuilds anyway so the final frame carries the freshest slice.
        var frame2 = new AnalysisFrame();
        var tiny = new float[100];
        Array.Fill(tiny, 0.1f);
        projector.Project(new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, tiny, tiny.Length, 0.2f),
            Array.Empty<DetectedEventUpdate>()), frame2);
        projector.AppendSnapshot(frame2, force: true);
        GraphSeriesFrame forced = Assert.Single(frame2.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopePcm);

        Assert.NotSame(first, forced);
    }

    [Fact]
    public void AppendSnapshot_PublishesOnlyLatestWindowSlice()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 32000);
        // ~3.125 s of audio: more than the 2 s publish window, so the slice must drop
        // the earliest samples rather than copying the whole retained buffer.
        var block = new float[150000];
        Array.Fill(block, 0.1f);
        var frame = new AnalysisFrame();
        projector.Project(new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.Synced, block, block.Length, 0.2f),
            Array.Empty<DetectedEventUpdate>()), frame);
        projector.AppendSnapshot(frame);

        GraphSeriesFrame pcm = Assert.Single(frame.ScopeSeries, s => s.Id == AnalysisGraphSeries.ScopePcm);
        // Newest tick = 150000; the 2 s window keeps only X >= 150000 - 96000 = 54000.
        Assert.NotEmpty(pcm.X);
        Assert.True(pcm.X[0] >= 54000.0, $"slice should start within the 2 s window, was {pcm.X[0]}");
        Assert.True(pcm.X[^1] <= 150000.0);
        // Genuinely a slice, not the full ~3 s (50000-point) retained buffer.
        Assert.True(pcm.X.Count < 50000, $"slice should be smaller than the full buffer, was {pcm.X.Count}");
    }

    [Fact]
    public void Project_NotSyncedClearsBeatSyncedFlag()
    {
        var projector = new ScopeRateFrameProjector(SampleRate, useCOnset: false, scopeSnapshotPointBudget: 256);
        var pcm = new float[960];
        var update = new DetectorMetricsBlockUpdate(
            Result(TgSyncStatus.NotSynced, pcm, pcm.Length, 0.0f),
            Array.Empty<DetectedEventUpdate>());
        var frame = new AnalysisFrame();

        projector.Project(update, frame);

        Assert.False(frame.BeatSynced);
    }
}
