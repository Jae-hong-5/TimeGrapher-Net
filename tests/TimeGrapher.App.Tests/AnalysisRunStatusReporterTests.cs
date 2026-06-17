using TimeGrapher.App.Services;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AnalysisRunStatusReporterTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void FirstThroughputChangeProducesStatusText()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame { BackgroundFps = 60, BackgroundSps = 48000, BackgroundSpf = 800 };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, droppedFrames: 0, SampleRate);

        Assert.Equal(
            "Backgroud Audio Thread Average - FPS:60, SPS:48000, SPF: 800 " +
            "Foregroud Audio Handler Average - FPS:0, SPS:0, SPF: 0",
            report.StatusText);
        Assert.Null(report.ConsoleWarning);
    }

    [Fact]
    public void UnchangedThroughputProducesNoStatusText()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame { BackgroundFps = 60, BackgroundSps = 48000, BackgroundSpf = 800 };

        reporter.Describe(frame, 0, SampleRate);
        AnalysisRunStatusReporter.Report second = reporter.Describe(frame, 0, SampleRate);

        Assert.Null(second.StatusText);
    }

    [Fact]
    public void InputOverrunOverridesWithOverrunMessage()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame { InputOverrun = true, InputSamplesDropped = 1234 };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal("Audio input overrun: dropped 1234 samples before analysis", report.StatusText);
    }

    [Fact]
    public void LargeAnalysisLagReportsLag()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame { AnalysisLagSamples = (ulong)SampleRate, ProcessingElapsedMs = 5.0 };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal("Analysis lag: 1000 ms (48000 samples), processing 5.0 ms", report.StatusText);
    }

    [Fact]
    public void DeadlineDegradationLevelShowsQualityReducedStatus()
    {
        var reporter = new AnalysisRunStatusReporter();
        // Lag already back under the warning threshold, but the monitor still
        // holds a reduced-quality level -> the sticky state must stay visible.
        var frame = new AnalysisFrame { DeadlineDegradationLevel = 2 };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal("Deadline pressure: rendering quality reduced (level 2/3)", report.StatusText);
    }

    [Fact]
    public void DroppedFramesWithoutChangeWarnsToConsoleOnly()
    {
        var reporter = new AnalysisRunStatusReporter();
        // No throughput change, no overrun, no lag -> only the coalesced-frames warning.
        AnalysisRunStatusReporter.Report report = reporter.Describe(new AnalysisFrame(), droppedFrames: 3, SampleRate);

        Assert.Null(report.StatusText);
        Assert.Equal("UI render coalesced 3 analysis frame(s)", report.ConsoleWarning);
    }

    [Fact]
    public void ResetClearsRememberedThroughput()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame { BackgroundFps = 60, BackgroundSps = 48000, BackgroundSpf = 800 };

        reporter.Describe(frame, 0, SampleRate);
        reporter.Reset();
        AnalysisRunStatusReporter.Report afterReset = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal(
            "Backgroud Audio Thread Average - FPS:60, SPS:48000, SPF: 800 " +
            "Foregroud Audio Handler Average - FPS:0, SPS:0, SPF: 0",
            afterReset.StatusText);
    }
}
