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

    // The tic/toc rate-error trace (RateTic/RateToc) moved to BeatMetricsFrameProjector
    // so it can be published per watch position; its emission/sharing is now covered by
    // BeatMetricsHistoryTests. This projector keeps only the scope, markers and results.

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
