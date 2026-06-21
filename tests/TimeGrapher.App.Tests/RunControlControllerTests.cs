using System.Collections.Generic;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// RunControlController lifts the live worker-knob forwarding (Scope Sweep multiple, active
/// watch position, Σ averaging) and the position-change auto-pause out of the MainWindow
/// code-behind. It forwards exactly those view-model edits, ignores everything else, and on a
/// position change forwards the position before auto-pausing an active run.
/// </summary>
public sealed class RunControlControllerTests
{
    private sealed class RecordingRunControls : IRunSessionControls, IRunCommandPause
    {
        public List<int> SweepMultiples { get; } = new();
        public List<WatchPosition> Positions { get; } = new();
        public List<bool> SigmaModes { get; } = new();
        public int PauseCalls { get; private set; }

        // Unified order log so a test can assert SetActivePosition runs before PauseIfRunning.
        public List<string> Calls { get; } = new();

        public void SetSweepMultiple(int sweepMultiple)
        {
            SweepMultiples.Add(sweepMultiple);
            Calls.Add("Sweep");
        }

        public void SetActivePosition(WatchPosition position)
        {
            Positions.Add(position);
            Calls.Add("Position");
        }

        public void SetSigmaAveraging(bool enabled)
        {
            SigmaModes.Add(enabled);
            Calls.Add("Sigma");
        }

        public void PauseIfRunning()
        {
            PauseCalls++;
            Calls.Add("Pause");
        }
    }

    private static (MainWindowViewModel Vm, RecordingRunControls Controls) Create()
    {
        var vm = new MainWindowViewModel();
        var controls = new RecordingRunControls();
        _ = new RunControlController(vm, controls, controls);
        return (vm, controls);
    }

    [Fact]
    public void SweepMultipleEdit_ForwardsToControls()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();

        vm.SweepMultiple += 1;

        Assert.Equal(new[] { vm.SweepMultiple }, controls.SweepMultiples);
    }

    [Fact]
    public void SelectedPositionIndexEdit_ForwardsActivePositionWithoutPausingWhenStopped()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();

        vm.SelectedPositionIndex = (int)WatchPosition.P6H;

        Assert.Equal(new[] { WatchPosition.P6H }, controls.Positions);
        Assert.Equal(0, controls.PauseCalls);
    }

    [Fact]
    public void SigmaAveragingEdit_ForwardsToControls()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();

        vm.SigmaAveraging = true;

        Assert.Equal(new[] { true }, controls.SigmaModes);
    }

    [Fact]
    public void AfterDetach_EditsAreNotForwarded()
    {
        var vm = new MainWindowViewModel();
        var controls = new RecordingRunControls();
        var controller = new RunControlController(vm, controls, controls);

        controller.Detach();
        vm.SweepMultiple += 1;
        vm.SigmaAveraging = true;

        Assert.Empty(controls.SweepMultiples);
        Assert.Empty(controls.SigmaModes);
    }

    [Fact]
    public void UnrelatedPropertyEdit_DoesNotForward()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();

        vm.Gain = 42.0;
        vm.StatusText = "Running";

        Assert.Empty(controls.SweepMultiples);
        Assert.Empty(controls.Positions);
        Assert.Empty(controls.SigmaModes);
        Assert.Equal(0, controls.PauseCalls);
    }

    [Fact]
    public void PositionChangeWhileRunningWithSetting_ForwardsThenPauses()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();
        vm.PauseOnPositionChange = true;
        vm.SetRunning();

        vm.SelectedPositionIndex = (int)WatchPosition.P6H;

        Assert.Equal(new[] { WatchPosition.P6H }, controls.Positions);
        Assert.Equal(1, controls.PauseCalls);
        // The forward must precede the auto-pause (the order the code-behind ran them in).
        Assert.Equal(new[] { "Position", "Pause" }, controls.Calls);
    }

    [Fact]
    public void PositionChangeWithSettingButNotRunning_DoesNotPause()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();
        vm.PauseOnPositionChange = true; // setting on, but still stopped

        vm.SelectedPositionIndex = (int)WatchPosition.P6H;

        Assert.Equal(0, controls.PauseCalls);
    }

    [Fact]
    public void PositionChangeWhileRunningWithoutSetting_DoesNotPause()
    {
        (MainWindowViewModel vm, RecordingRunControls controls) = Create();
        vm.SetRunning(); // running, but the auto-pause option is off

        vm.SelectedPositionIndex = (int)WatchPosition.P6H;

        Assert.Equal(0, controls.PauseCalls);
    }
}
