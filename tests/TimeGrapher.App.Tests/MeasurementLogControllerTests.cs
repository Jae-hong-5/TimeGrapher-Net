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
/// code-behind: it opens/closes the sink on the IsMeasurementLogEnabled toggle, consumes the
/// CLI --measurement-log path once, forwards displayed frames, and stops on dispose. The sink
/// is created through a factory so these tests never open a CSV file.
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
        public List<FakeSink> Sinks { get; } = new();

        public IMeasurementResultSink Create(string path)
        {
            Paths.Add(path);
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
    public void StartupEnabledWithPendingPath_OpensThePendingPath()
    {
        var factory = new RecordingFactory();

        // A bare filename has no parent directory, so EnsureParentDirectory touches no disk.
        using var controller = new MeasurementLogController(EnabledViewModel(true), "seed.csv", factory.Create);

        Assert.Equal(new[] { "seed.csv" }, factory.Paths);
    }

    [Fact]
    public void StartupDisabled_OpensNothingAndObserveIsNoOp()
    {
        var factory = new RecordingFactory();

        using var controller = new MeasurementLogController(EnabledViewModel(false), "seed.csv", factory.Create);
        controller.ObserveDisplayed(new AnalysisFrame());

        Assert.Empty(factory.Paths);
    }

    [Fact]
    public void PendingPathIsConsumedOnlyForTheFirstSession()
    {
        // The second session takes the default branch, which creates <bin>/log; remember whether
        // it pre-existed so the cleanup only removes a directory this test created and left empty
        // (the fake sink writes no file).
        string logDir = Path.Combine(AppContext.BaseDirectory, "log");
        bool logDirPreexisted = Directory.Exists(logDir);

        try
        {
            var vm = EnabledViewModel(true);
            var factory = new RecordingFactory();
            using var controller = new MeasurementLogController(vm, "seed.csv", factory.Create);

            vm.IsMeasurementLogEnabled = false; // closes the first session
            Assert.True(factory.Sinks[0].Disposed);

            vm.IsMeasurementLogEnabled = true;  // a second session must NOT reuse the CLI path

            Assert.Equal(2, factory.Paths.Count);
            Assert.Equal("seed.csv", factory.Paths[0]);
            Assert.NotEqual("seed.csv", factory.Paths[1]);
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
    public void ObserveDisplayed_ForwardsToTheActiveSink()
    {
        var factory = new RecordingFactory();
        using var controller = new MeasurementLogController(EnabledViewModel(true), "seed.csv", factory.Create);

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

        controller.Dispose();

        Assert.True(factory.Sinks[0].Disposed);

        // Detached: a later toggle creates no new sink.
        vm.IsMeasurementLogEnabled = false;
        vm.IsMeasurementLogEnabled = true;

        Assert.Single(factory.Paths);
    }
}
