using System.Collections.Generic;
using System.Threading.Tasks;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// AcceptBandController lifts the accept-band seed + live-apply flow out of the
/// MainWindow code-behind. It must seed the view-model from the persisted bands on
/// construction without raising a save, then forward only the five band-property edits
/// to the operations — the trigger logic that was previously untestable inside the View.
/// </summary>
public sealed class AcceptBandControllerTests
{
    private sealed class FakeAcceptBandOperations : IAcceptBandOperations
    {
        public AcceptBandValues CurrentBands { get; set; }
        public List<AcceptBandValues> Applied { get; } = new();
        public bool Result { get; set; } = true;

        public bool TryApplyEditedBands(AcceptBandValues candidate)
        {
            Applied.Add(candidate);
            return Result;
        }

        public void ApplyCurrentBands() { }
    }

    private static MainWindowViewModel CreateViewModel() => new();

    private static FakeAcceptBandOperations SeededOperations() => new()
    {
        CurrentBands = new AcceptBandValues(
            RateMinSPerDay: -3.0,
            RateMaxSPerDay: 7.0,
            AmplitudeMinDeg: 250.0,
            AmplitudeMaxDeg: 310.0,
            BeatErrorMagnitudeMs: 0.9),
    };

    [Fact]
    public void Construction_SeedsViewModelFromPersistedBands()
    {
        MainWindowViewModel vm = CreateViewModel();
        FakeAcceptBandOperations ops = SeededOperations();

        _ = new AcceptBandController(vm, ops);

        Assert.Equal(-3.0m, vm.RateAcceptMin);
        Assert.Equal(7.0m, vm.RateAcceptMax);
        Assert.Equal(250.0m, vm.AmplitudeAcceptMin);
        Assert.Equal(310.0m, vm.AmplitudeAcceptMax);
        Assert.Equal(0.9m, vm.BeatErrorAcceptMag);
    }

    [Fact]
    public void Construction_DoesNotApply()
    {
        MainWindowViewModel vm = CreateViewModel();
        FakeAcceptBandOperations ops = SeededOperations();

        _ = new AcceptBandController(vm, ops);

        Assert.Empty(ops.Applied);
    }

    [Fact]
    public void EditingABand_ForwardsAllFiveCurrentValuesOnce()
    {
        MainWindowViewModel vm = CreateViewModel();
        FakeAcceptBandOperations ops = SeededOperations();
        _ = new AcceptBandController(vm, ops);

        vm.RateAcceptMin = -5.0m;

        AcceptBandValues applied = Assert.Single(ops.Applied);
        Assert.Equal(-5.0, applied.RateMinSPerDay);
        Assert.Equal(7.0, applied.RateMaxSPerDay);
        Assert.Equal(250.0, applied.AmplitudeMinDeg);
        Assert.Equal(310.0, applied.AmplitudeMaxDeg);
        Assert.Equal(0.9, applied.BeatErrorMagnitudeMs);
    }

    [Fact]
    public void EditingEachBandProperty_ForwardsOncePerEdit()
    {
        MainWindowViewModel vm = CreateViewModel();
        FakeAcceptBandOperations ops = SeededOperations();
        _ = new AcceptBandController(vm, ops);

        vm.RateAcceptMin = -5.0m;
        vm.RateAcceptMax = 9.0m;
        vm.AmplitudeAcceptMin = 240.0m;
        vm.AmplitudeAcceptMax = 320.0m;
        vm.BeatErrorAcceptMag = 1.2m;

        Assert.Equal(5, ops.Applied.Count);
        // Counting calls is not enough: the controller could forward stale or wrong
        // values on any edit. The final candidate must carry every edited band value.
        AcceptBandValues last = ops.Applied[^1];
        Assert.Equal(-5.0, last.RateMinSPerDay);
        Assert.Equal(9.0, last.RateMaxSPerDay);
        Assert.Equal(240.0, last.AmplitudeMinDeg);
        Assert.Equal(320.0, last.AmplitudeMaxDeg);
        Assert.Equal(1.2, last.BeatErrorMagnitudeMs);
    }

    [Fact]
    public void AfterDetach_BandEditsAreNotForwarded()
    {
        MainWindowViewModel vm = CreateViewModel();
        FakeAcceptBandOperations ops = SeededOperations();
        var controller = new AcceptBandController(vm, ops);

        controller.Detach();
        vm.RateAcceptMin = -5.0m;

        Assert.Empty(ops.Applied);
    }

    [Fact]
    public void EditingANonBandProperty_DoesNotForward()
    {
        MainWindowViewModel vm = CreateViewModel();
        FakeAcceptBandOperations ops = SeededOperations();
        _ = new AcceptBandController(vm, ops);

        vm.Gain = 42.0;
        vm.StatusText = "Running";
        vm.UseCOnset = true;

        Assert.Empty(ops.Applied);
    }
}
