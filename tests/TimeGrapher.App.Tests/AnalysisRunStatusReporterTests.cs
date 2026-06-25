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
            "BG - FPS:60, SPS:48000, SPF: 800 " +
            "FG - FPS:0, SPS:0, SPF: 0",
            report.StatusText);
        Assert.Null(report.ConsoleWarning);
    }

    [Fact]
    public void ForegroundThroughputChangeIsReportedWithoutARefreshFlag()
    {
        // Foreground stats now ride every frame (coalescing-safe). A change must
        // be reported off the carried values alone, so the status bar never goes
        // stale when the 2-second refresh frame is coalesced away.
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame
        {
            BackgroundFps = 60,
            BackgroundSps = 48000,
            BackgroundSpf = 800,
            ForegroundFps = 50,
            ForegroundSps = 48000,
            ForegroundSpf = 960,
        };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, droppedFrames: 0, SampleRate);

        Assert.Equal(
            "BG - FPS:60, SPS:48000, SPF: 800 " +
            "FG - FPS:50, SPS:48000, SPF: 960",
            report.StatusText);
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

        Assert.Equal(UserErrorMessages.AudioInputInterrupted, report.StatusText);
        Assert.Equal("Audio input overrun: dropped 1234 samples before analysis.", report.LogDetail);
    }

    [Fact]
    public void LargeAnalysisLagReportsLag()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame { AnalysisLagSamples = (ulong)SampleRate, ProcessingElapsedMs = 5.0 };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal(UserErrorMessages.AnalysisRunningBehind, report.StatusText);
        Assert.Equal("Analysis lag: 1000 ms (48000 samples), processing 5.0 ms.", report.LogDetail);
    }

    [Fact]
    public void DeadlineDegradationLevelShowsQualityReducedStatus()
    {
        var reporter = new AnalysisRunStatusReporter();
        // Lag already back under the warning threshold, but the monitor still
        // holds a reduced-quality level -> the sticky state must stay visible.
        var frame = new AnalysisFrame { DeadlineDegradationLevel = 2 };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal(UserErrorMessages.DisplayQualityReduced, report.StatusText);
        Assert.Equal("Deadline pressure: rendering quality reduced (level 2/3).", report.LogDetail);
    }

    [Fact]
    public void DroppedFramesWithoutChangeWarnsToConsoleOnly()
    {
        var reporter = new AnalysisRunStatusReporter();
        // No throughput change, no overrun, no lag -> only the coalesced-frames warning.
        AnalysisRunStatusReporter.Report report = reporter.Describe(new AnalysisFrame(), droppedFrames: 3, SampleRate);

        Assert.Null(report.StatusText);
        Assert.Equal("UI render coalesced 3 analysis frame(s)", report.ConsoleWarning);
        Assert.Null(report.LogDetail);
    }

    [Fact]
    public void SignalQualityWarningReportsRecoveryGuidance()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Quality = SignalQualityFlags.PossibleFalseC | SignalQualityFlags.CTimingUnstable,
            },
        };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal("Possible false C marker. Check Beat Noise and reduce handling noise.", report.StatusText);
        Assert.Equal("Signal quality warning: Possible false C.", report.LogDetail);
    }

    [Fact]
    public void TrainedClassifierVerdictSurfacesThroughTheSameStatusPath()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame
        {
            SignalQuality = new SignalQualityAssessment(SignalQualityClass.Noisy, 0.9f, default),
        };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal("Signal looks noisy. Reduce ambient or handling noise.", report.StatusText);
        Assert.Equal("Signal quality warning: Noisy signal.", report.LogDetail);
    }

    [Fact]
    public void LowConfidenceClassifierVerdictRaisesNoWarning()
    {
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame
        {
            SignalQuality = new SignalQualityAssessment(SignalQualityClass.Noisy, 0.2f, default),
        };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Null(report.StatusText);
        Assert.Null(report.LogDetail);
    }

    [Fact]
    public void PerBeatAndClassifierFlagsAreOredWithPriorityWinning()
    {
        // Per-beat raises WeakSignal, the classifier raises Noisy: the OR'd value is
        // WeakSignal|NoisySignal, and the SignalQualityText priority ladder surfaces
        // the higher-priority Noisy guidance.
        var reporter = new AnalysisRunStatusReporter();
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot { Quality = SignalQualityFlags.WeakSignal },
            SignalQuality = new SignalQualityAssessment(SignalQualityClass.Noisy, 0.9f, default),
        };

        AnalysisRunStatusReporter.Report report = reporter.Describe(frame, 0, SampleRate);

        Assert.Equal("Signal looks noisy. Reduce ambient or handling noise.", report.StatusText);
        Assert.Equal("Signal quality warning: Noisy signal.", report.LogDetail);
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
            "BG - FPS:60, SPS:48000, SPF: 800 " +
            "FG - FPS:0, SPS:0, SPF: 0",
            afterReset.StatusText);
    }
}
