using System.Collections.Generic;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// SamplingSettingsController seeds the Settings inputs from the persisted sampling
/// parameters without raising a save, then persists only valid, changed edits of the two
/// sampling properties. There is no live re-apply (the values are read at the next run
/// start), so the controller's whole job is the seed + validate + persist gate — exactly
/// the logic that was previously untestable inside the View. Mirrors
/// <see cref="AcceptBandControllerTests"/>.
/// </summary>
public sealed class SamplingSettingsControllerTests
{
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

        Assert.Equal(8192, vm.AnalysisBlockSize);
        Assert.Equal(50, vm.CaptureBufferMs);
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

        vm.AnalysisBlockSize = 8192;

        SamplingSettings saved = Assert.Single(persisted);
        Assert.Equal(8192, saved.AnalysisBlockSize);
        Assert.Equal(20, saved.CaptureBufferMs);
    }

    [Fact]
    public void EditingCaptureBuffer_PersistsBothCurrentValues()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.CaptureBufferMs = 50;

        SamplingSettings saved = Assert.Single(persisted);
        Assert.Equal(4096, saved.AnalysisBlockSize);
        Assert.Equal(50, saved.CaptureBufferMs);
    }

    [Fact]
    public void OutOfRangeEdit_IsNotPersisted()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, _) = Build(new SamplingSettings(4096, 20));

        vm.AnalysisBlockSize = 16; // below the editable floor

        Assert.Empty(persisted);
    }

    [Fact]
    public void AfterDetach_EditsAreNotPersisted()
    {
        (List<SamplingSettings> persisted, MainWindowViewModel vm, SamplingSettingsController controller) =
            Build(new SamplingSettings(4096, 20));

        controller.Detach();
        vm.AnalysisBlockSize = 8192;

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
