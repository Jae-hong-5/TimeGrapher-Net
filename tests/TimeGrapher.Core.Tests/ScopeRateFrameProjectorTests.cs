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
        Assert.True(pcmSeries.X.Count > 0 && pcmSeries.X.Count <= 256);
        Assert.Equal(pcmSeries.X.Count, pcmSeries.Y.Count);
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
            Result(TgSyncStatus.Synced, Array.Empty<float>(), 0, 0.2f), events);
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
        Assert.True(pcmSeries.X.Count <= 8);
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
