using System.Collections.Generic;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// RunControlController lifts the live worker-knob forwarding (Scope Sweep multiple, active
/// watch position, Σ averaging) out of the MainWindow code-behind. It must forward exactly
/// those three view-model edits to the controls and ignore everything else.
/// </summary>
public sealed class RunControlControllerTests
{
    private sealed class FakeRunSessionControls : IRunSessionControls
    {
        public List<int> SweepMultiples { get; } = new();
        public List<WatchPosition> Positions { get; } = new();
        public List<bool> SigmaModes { get; } = new();

        public void SetSweepMultiple(int sweepMultiple) => SweepMultiples.Add(sweepMultiple);
        public void SetActivePosition(WatchPosition position) => Positions.Add(position);
        public void SetSigmaAveraging(bool enabled) => SigmaModes.Add(enabled);
    }

    private static (MainWindowViewModel Vm, FakeRunSessionControls Controls) Create()
    {
        var vm = new MainWindowViewModel();
        var controls = new FakeRunSessionControls();
        _ = new RunControlController(vm, controls);
        return (vm, controls);
    }

    [Fact]
    public void SweepMultipleEdit_ForwardsToControls()
    {
        (MainWindowViewModel vm, FakeRunSessionControls controls) = Create();

        vm.SweepMultiple += 1;

        Assert.Equal(new[] { vm.SweepMultiple }, controls.SweepMultiples);
    }

    [Fact]
    public void SelectedPositionIndexEdit_ForwardsActivePosition()
    {
        (MainWindowViewModel vm, FakeRunSessionControls controls) = Create();

        vm.SelectedPositionIndex = (int)WatchPosition.P6H;

        Assert.Equal(new[] { WatchPosition.P6H }, controls.Positions);
    }

    [Fact]
    public void SigmaAveragingEdit_ForwardsToControls()
    {
        (MainWindowViewModel vm, FakeRunSessionControls controls) = Create();

        vm.SigmaAveraging = true;

        Assert.Equal(new[] { true }, controls.SigmaModes);
    }

    [Fact]
    public void UnrelatedPropertyEdit_DoesNotForward()
    {
        (MainWindowViewModel vm, FakeRunSessionControls controls) = Create();

        vm.Gain = 42.0;
        vm.StatusText = "Running";

        Assert.Empty(controls.SweepMultiples);
        Assert.Empty(controls.Positions);
        Assert.Empty(controls.SigmaModes);
    }
}
