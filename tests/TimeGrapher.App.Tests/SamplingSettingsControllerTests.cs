using System;
using System.Collections.Generic;
using TimeGrapher.App;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// SamplingSettingsController seeds the Settings inputs from the persisted sampling
/// parameters without raising a save, then on each edit snaps the input to an in-range,
/// step-aligned value (writing it back so the UI shows what will be used), persists valid
/// changed edits, and keeps SamplingSettings.Current in sync. There is no live re-apply.
/// The shared static is saved/restored per test so edits do not leak across tests.
/// </summary>
public sealed class SamplingSettingsControllerTests : IDisposable
{
    private readonly SamplingSettings _savedCurrent = SamplingSettings.Current;

    public void Dispose() => SamplingSettings.Current = _savedCurrent;

    private static (List<SamplingSettings> persisted, MainWindowViewModel vm, SamplingSettingsController controller) Build(
        SamplingSettings initial)
    {
        var vm = new MainWindowViewModel();
        var persisted = new List<SamplingSettings>();
        var controller = new SamplingSettingsController(vm, initial, persisted.Add);
        return (persisted, vm, controller);
    }

    [Fact]
    public void Construction_SeedsViewModelFromPersistedValues()
    {
        (_, MainWindowViewModel vm, _) = Build(new SamplingSettings(8192, 50));

        Assert.Equal(8192m, vm.AnalysisBlockSize);
        Assert.Equal(50m, vm.CaptureBufferMs);
    }

    [Fact]
    public void Construction_DoesNotPersist()
    {
        (List<SamplingSettings> persisted, _, _) = Build(new SamplingSettings(8192, 50));

        Assert.Empty(persisted);
    }

    [Fact]
    public void EditingBlockSize_PersistsBothCurrentValues()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.AnalysisBlockSize = 8192m;

        SamplingSettings saved = Assert.Single(persisted);
        Assert.Equal(8192, saved.AnalysisBlockSize);
        Assert.Equal(20, saved.CaptureBufferMs);
        Assert.Equal(8192m, vm.AnalysisBlockSize);
    }

    [Fact]
    public void EditingCaptureBuffer_PersistsBothCurrentValues()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.CaptureBufferMs = 50m;

        SamplingSettings saved = Assert.Single(persisted);
        Assert.Equal(4096, saved.AnalysisBlockSize);
        Assert.Equal(50, saved.CaptureBufferMs);
        Assert.Equal(50m, vm.CaptureBufferMs);
    }

    [Fact]
    public void OffStepBlockEdit_SnapsBackAndPersistsTheSnappedValue()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.AnalysisBlockSize = 257m;

        Assert.Equal(256m, vm.AnalysisBlockSize); // snapped back so the UI shows what is used
        SamplingSettings saved = Assert.Single(persisted);
        Assert.Equal(256, saved.AnalysisBlockSize);
    }

    [Fact]
    public void OffStepBufferEdit_SnapsBackAndPersistsTheSnappedValue()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.CaptureBufferMs = 6m;

        Assert.Equal(5m, vm.CaptureBufferMs);
        SamplingSettings saved = Assert.Single(persisted);
        Assert.Equal(5, saved.CaptureBufferMs);
    }

    [Fact]
    public void OutOfRangeBlockEdit_ClampsToCeilingAndPersists()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.AnalysisBlockSize = 100000m;

        Assert.Equal(16384m, vm.AnalysisBlockSize);
        Assert.Equal(16384, Assert.Single(persisted).AnalysisBlockSize);
    }

    [Fact]
    public void SnapToCurrentValue_DoesNotPersist()
    {
        // 4100 normalizes back to the current 4096, so it snaps the input but is a no-op edit.
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.AnalysisBlockSize = 4100m;

        Assert.Equal(4096m, vm.AnalysisBlockSize);
        Assert.Empty(persisted);
    }

    [Fact]
    public void AcceptedEdit_UpdatesSharedCurrentSnapshot()
    {
        (_, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.AnalysisBlockSize = 8192m;

        Assert.Equal(new SamplingSettings(8192, 20), SamplingSettings.Current);
    }

    [Fact]
    public void AfterDetach_EditsAreNotPersisted()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, SamplingSettingsController controller) =
            Build(new SamplingSettings(4096, 20));

        controller.Detach();
        vm.AnalysisBlockSize = 8192m;

        Assert.Empty(persisted);
    }

    [Fact]
    public void EditingANonSamplingProperty_DoesNotPersist()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.Gain = 42.0;
        vm.UseCOnset = true;

        Assert.Empty(persisted);
    }
}
