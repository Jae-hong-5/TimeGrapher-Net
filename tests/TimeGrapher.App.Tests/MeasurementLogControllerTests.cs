using System;
using System.Collections.Generic;
using System.IO;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// MeasurementLogController lifts the measurement-log lifecycle out of the MainWindow
/// code-behind: it opens a fresh sink at each run start (capturing that run's lift angle),
/// keeps writing across a pause/resume, consumes the CLI --measurement-log path for the first
/// run, forwards displayed frames, and closes on disable/dispose. The sink is created through a
/// factory so these tests never open a CSV file.
/// </summary>
public sealed class MeasurementLogControllerTests
{
    private sealed class FakeSink : IMeasurementResultSink
    {
        public int ObserveCalls { get; private set; }
        public bool Disposed { get; private set; }

        public void ObserveDisplayed(AnalysisFrame frame) => ObserveCalls++;
        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingFactory
    {
        public List<string> Paths { get; } = new();
        public List<decimal> LiftAngles { get; } = new();
        public List<FakeSink> Sinks { get; } = new();

        public IMeasurementResultSink Create(string path, decimal liftAngleDeg)
        {
            Paths.Add(path);
            LiftAngles.Add(liftAngleDeg);
            var sink = new FakeSink();
            Sinks.Add(sink);
            return sink;
        }
    }

    private static MainWindowViewModel EnabledViewModel(bool enabled)
    {
        var vm = new MainWindowViewModel();
        vm.IsMeasurementLogEnabled = enabled;
        return vm;
    }

    [Fact]
    public void BuildLogPath_UsesLogFolderAndTimestamp()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "TimeGrapher.App");
        var timestamp = new DateTime(2026, 6, 18, 14, 5, 7);

        string path = MeasurementLogController.BuildLogPath(baseDirectory, timestamp);

        Assert.Equal(Path.Combine(baseDirectory, "log", "20260618_140507.csv"), path);
    }

    [Fact]
    public void EnabledButNotRunning_OpensNothing()
    {
        var factory = new RecordingFactory();

        // Enabled while stopped: the log waits for a run start, so no sink yet.
        using var controller = new MeasurementLogController(EnabledViewModel(true), "seed.csv", factory.Create);

        Assert.Empty(factory.Paths);
    }

    [Fact]
    public void RunStart_OpensPendingPathWithCurrentLiftAngle()
    {
        var factory = new RecordingFactory();
        var vm = EnabledViewModel(true);
        vm.LiftAngle = 54m;

        // A bare filename has no parent directory, so EnsureParentDirectory touches no disk.
        using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);
        vm.SetRunning();

        Assert.Equal(new[] { "seed.csv" }, factory.Paths);
        Assert.Equal(new[] { 54m }, factory.LiftAngles);
    }

    [Fact]
    public void EachRunStart_RecordsTheLiftAngleThatRunUses()
    {
        // The point of capturing at run start: a lift-angle edit made while stopped (after the
        // log was enabled) reaches the next run's header, and the pending CLI path is consumed
        // only by the first run. The second run takes the default branch, which creates <bin>/log;
        // remember whether it pre-existed so the cleanup only removes a directory this test made.
        string logDir = Path.Combine(AppContext.BaseDirectory, "log");
        bool logDirPreexisted = Directory.Exists(logDir);

        try
        {
            var vm = EnabledViewModel(true);
            vm.LiftAngle = 52m;
            var factory = new RecordingFactory();
            using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);

            vm.SetRunning();            // run 1 captures 52 to the pending path
            vm.SetStopped();
            vm.LiftAngle = 60m;         // edited while stopped, log still enabled
            vm.SetRunning();            // run 2 captures 60 to a fresh timestamped file

            Assert.Equal(new[] { 52m, 60m }, factory.LiftAngles);
            Assert.Equal("seed.csv", factory.Paths[0]);
            Assert.NotEqual("seed.csv", factory.Paths[1]);
            Assert.True(factory.Sinks[0].Disposed); // run 1's sink closed when run 2 opened
        }
        finally
        {
            if (!logDirPreexisted && Directory.Exists(logDir) &&
                Directory.GetFileSystemEntries(logDir).Length == 0)
            {
                Directory.Delete(logDir);
            }
        }
    }

    [Fact]
    public void ResumeFromPause_KeepsTheSameLogFile()
    {
        var vm = EnabledViewModel(true);
        var factory = new RecordingFactory();
        using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);

        vm.SetRunning();   // opens the run's log
        vm.SetPaused();
        vm.SetRunning();   // resume must NOT open a new file

        Assert.Single(factory.Paths);
    }

    [Fact]
    public void Disable_ClosesTheOpenLog()
    {
        var vm = EnabledViewModel(true);
        var factory = new RecordingFactory();
        using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);

        vm.SetRunning();
        vm.SetStopped();
        vm.IsMeasurementLogEnabled = false;

        Assert.True(factory.Sinks[0].Disposed);
    }

    [Fact]
    public void Disabled_RunStartOpensNothingAndObserveIsNoOp()
    {
        var factory = new RecordingFactory();
        var vm = EnabledViewModel(false);

        using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);
        vm.SetRunning();
        controller.ObserveDisplayed(new AnalysisFrame());

        Assert.Empty(factory.Paths);
    }

    [Fact]
    public void ObserveDisplayed_ForwardsToTheActiveSink()
    {
        var factory = new RecordingFactory();
        var vm = EnabledViewModel(true);
        using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);
        vm.SetRunning();

        controller.ObserveDisplayed(new AnalysisFrame());
        controller.ObserveDisplayed(new AnalysisFrame());

        Assert.Equal(2, factory.Sinks[0].ObserveCalls);
    }

    [Fact]
    public void Dispose_DisposesTheSinkAndStopsReacting()
    {
        var vm = EnabledViewModel(true);
        var factory = new RecordingFactory();
        var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);
        vm.SetRunning();

        controller.Dispose();

        Assert.True(factory.Sinks[0].Disposed);

        // Detached: a later run start creates no new sink.
        vm.SetStopped();
        vm.SetRunning();

        Assert.Single(factory.Paths);
    }
}
