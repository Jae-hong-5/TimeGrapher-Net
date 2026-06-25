using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Services;
using TimeGrapher.App.Tabs;
using TimeGrapher.App.Views;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// GraphAcceptBandOperations is the render/persist side moved out of the MainWindow
/// code-behind. These tests cover the parts that do not touch the shared
/// AcceptBandSettings.Current: the seed read, and the two gate-rejection paths
/// (an inverted edit is invalid; re-applying the current band is a no-op). The accepted
/// path's constituents — ShouldReplace, the store round-trip, and the fan-out — are each
/// covered by AcceptBandSettingsTests, AppSettingsStoreTests, and
/// GraphFrameRendererAcceptBandTests.
/// </summary>
public sealed class GraphAcceptBandOperationsTests
{
    private sealed class BandConsumer : IAnalysisFrameConsumer, IAcceptBandConsumer
    {
        public int ApplyCalls { get; private set; }
        public string TabId => "band";
        public void Initialize(AnalysisTabResetContext context) { }
        public void Reset(AnalysisTabResetContext context) { }
        public void ObserveFrame(AnalysisFrame frame) { }
        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context) { }
        public void ApplyAcceptBands() => ApplyCalls++;
    }

    private static (GraphAcceptBandOperations Ops, BandConsumer Band) Create()
    {
        var band = new BandConsumer();
        var renderer = new GraphFrameRenderer(new IAnalysisFrameConsumer[] { band }, new TextBlock());
        return (new GraphAcceptBandOperations(renderer), band);
    }

    [Fact]
    public void CurrentBands_ReflectsAcceptBandSettingsCurrent()
    {
        (GraphAcceptBandOperations ops, _) = Create();
        AcceptBandSettings current = AcceptBandSettings.Current;

        AcceptBandValues bands = ops.CurrentBands;

        Assert.Equal(current.RateMinSPerDay, bands.RateMinSPerDay);
        Assert.Equal(current.RateMaxSPerDay, bands.RateMaxSPerDay);
        Assert.Equal(current.AmplitudeMinDeg, bands.AmplitudeMinDeg);
        Assert.Equal(current.AmplitudeMaxDeg, bands.AmplitudeMaxDeg);
        Assert.Equal(current.BeatErrorMagnitudeMs, bands.BeatErrorMagnitudeMs);
    }

    [Fact]
    public void TryApplyEditedBands_InvertedCandidate_ReturnsFalseAndDoesNotFanOut()
    {
        (GraphAcceptBandOperations ops, BandConsumer band) = Create();

        // min above max -> AcceptBandSettings.IsValid is false -> ShouldReplace false,
        // regardless of the current band, so this never replaces the shared Current.
        bool applied = ops.TryApplyEditedBands(new AcceptBandValues(
            RateMinSPerDay: 10.0,
            RateMaxSPerDay: -10.0,
            AmplitudeMinDeg: 300.0,
            AmplitudeMaxDeg: 270.0,
            BeatErrorMagnitudeMs: 0.5));

        Assert.False(applied);
        Assert.Equal(0, band.ApplyCalls);
    }

    [Fact]
    public void TryApplyEditedBands_NoOpCandidate_ReturnsFalseAndDoesNotFanOut()
    {
        (GraphAcceptBandOperations ops, BandConsumer band) = Create();

        // Re-applying the current band is a no-op (ShouldReplace requires a change),
        // so this reads Current but never replaces it.
        bool applied = ops.TryApplyEditedBands(ops.CurrentBands);

        Assert.False(applied);
        Assert.Equal(0, band.ApplyCalls);
    }

    [Fact]
    public void TryApplyEditedBands_ValidChange_SwapsPersistsAndFansOut()
    {
        AcceptBandSettings original = AcceptBandSettings.Current;
        try
        {
            // Known starting band so the candidate is a genuine change (test runs serially, so the
            // static Current swap is safe; restored in finally). Persistence is injected, so no file.
            AcceptBandSettings.Current = AcceptBandSettings.Default;
            var band = new BandConsumer();
            var renderer = new GraphFrameRenderer(new IAnalysisFrameConsumer[] { band }, new TextBlock());
            AcceptBandSettings? persisted = null;
            var ops = new GraphAcceptBandOperations(renderer, b => persisted = b);

            var candidate = new AcceptBandValues(
                RateMinSPerDay: AcceptBandSettings.Default.RateMinSPerDay - 1.0, // changed, still valid
                RateMaxSPerDay: AcceptBandSettings.Default.RateMaxSPerDay,
                AmplitudeMinDeg: AcceptBandSettings.Default.AmplitudeMinDeg,
                AmplitudeMaxDeg: AcceptBandSettings.Default.AmplitudeMaxDeg,
                BeatErrorMagnitudeMs: AcceptBandSettings.Default.BeatErrorMagnitudeMs);

            bool applied = ops.TryApplyEditedBands(candidate);

            Assert.True(applied);
            Assert.Equal(candidate.RateMinSPerDay, AcceptBandSettings.Current.RateMinSPerDay); // swapped
            Assert.NotNull(persisted);                                                         // persisted
            Assert.Equal(candidate.RateMinSPerDay, persisted!.RateMinSPerDay);
            Assert.Equal(1, band.ApplyCalls);                                                  // fanned out
        }
        finally
        {
            AcceptBandSettings.Current = original;
        }
    }

    [Fact]
    public void ApplyCurrentBands_FansOutWithoutPersisting()
    {
        var band = new BandConsumer();
        var renderer = new GraphFrameRenderer(new IAnalysisFrameConsumer[] { band }, new TextBlock());
        AcceptBandSettings? persisted = null;
        var ops = new GraphAcceptBandOperations(renderer, b => persisted = b);

        ops.ApplyCurrentBands();

        Assert.Null(persisted);
        Assert.Equal(1, band.ApplyCalls);
    }
}
