using System;
using System.Globalization;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Turns per-frame throughput / lag / overrun information into the status-bar text.
/// Tracks the last reported background/foreground rates so it only emits a new
/// throughput line when something actually changed. Extracted from MainWindow so the
/// formatting and change-detection are unit-testable and out of the view.
/// </summary>
internal sealed class AnalysisRunStatusReporter
{
    private double _backgroundFps;
    private double _backgroundSps;
    private double _backgroundSpf;
    private double _foregroundFps;
    private double _foregroundSps;
    private double _foregroundSpf;

    /// <summary>Result of describing a frame.</summary>
    /// <param name="StatusText">New status-bar text, or null to leave it unchanged.</param>
    /// <param name="ConsoleWarning">Diagnostic to write to stderr, or null.</param>
    internal readonly record struct Report(string? StatusText, string? ConsoleWarning, string? LogDetail);

    public void Reset()
    {
        _backgroundFps = _backgroundSps = _backgroundSpf = 0.0;
        _foregroundFps = _foregroundSps = _foregroundSpf = 0.0;
    }

    public Report Describe(AnalysisFrame frame, ulong droppedFrames, int sampleRate)
    {
        bool statusUpdated = false;
        if (_backgroundFps != frame.BackgroundFps ||
            _backgroundSps != frame.BackgroundSps ||
            _backgroundSpf != frame.BackgroundSpf)
        {
            _backgroundFps = frame.BackgroundFps;
            _backgroundSps = frame.BackgroundSps;
            _backgroundSpf = frame.BackgroundSpf;
            statusUpdated = true;
        }

        // Foreground stats ride every frame (coalescing-safe), so compare them
        // like the background stats: a change is reported even on a frame that
        // is not the 2-second refresh frame.
        if (_foregroundFps != frame.ForegroundFps ||
            _foregroundSps != frame.ForegroundSps ||
            _foregroundSpf != frame.ForegroundSpf)
        {
            _foregroundFps = frame.ForegroundFps;
            _foregroundSps = frame.ForegroundSps;
            _foregroundSpf = frame.ForegroundSpf;
            statusUpdated = true;
        }

        string? statusText = statusUpdated ? FormatThroughput() : null;
        string? consoleWarning = null;
        string? logDetail = null;

        if (frame.InputOverrun)
        {
            statusText = UserErrorMessages.AudioInputInterrupted;
            logDetail = "Audio input overrun: dropped " +
                        frame.InputSamplesDropped.ToString(CultureInfo.InvariantCulture) +
                        " samples before analysis.";
        }
        else if (frame.AnalysisLagSamples > (ulong)Math.Max(1, sampleRate / 4))
        {
            double lagMs = frame.AnalysisLagSamples * 1000.0 / Math.Max(1, sampleRate);
            statusText = UserErrorMessages.AnalysisRunningBehind;
            logDetail = string.Format(
                CultureInfo.InvariantCulture,
                "Analysis lag: {0:F0} ms ({1} samples), processing {2:F1} ms.",
                lagMs,
                frame.AnalysisLagSamples,
                frame.ProcessingElapsedMs);
        }
        else if (frame.DeadlineDegradationLevel > 0)
        {
            // Sticky state from the analysis-side deadline monitor: lag may have
            // subsided below the warning threshold while quality is still reduced.
            statusText = UserErrorMessages.DisplayQualityReduced;
            logDetail = string.Format(
                CultureInfo.InvariantCulture,
                "Deadline pressure: rendering quality reduced (level {0}/{1}).",
                frame.DeadlineDegradationLevel,
                AnalysisDeadlineMonitor.MaxLevel);
        }
        else if (CombinedSignalQuality(frame) is SignalQualityFlags quality && quality != SignalQualityFlags.None)
        {
            statusText = SignalQualityText.Guidance(quality);
            logDetail = "Signal quality warning: " + SignalQualityText.Summary(quality) + ".";
        }
        else if (droppedFrames != 0)
        {
            consoleWarning = "UI render coalesced " +
                             droppedFrames.ToString(CultureInfo.InvariantCulture) +
                             " analysis frame(s)";
        }

        return new Report(statusText, consoleWarning, logDetail);
    }

    // The per-beat rule flags (BeatSegments.Quality) and the optional trained
    // classifier's window-level verdict (SignalQuality) both express themselves as
    // the same SignalQualityFlags; OR them so one status path covers both producers.
    // When the ML feature is off, SignalQuality is null and the map contributes None.
    private static SignalQualityFlags CombinedSignalQuality(AnalysisFrame frame)
        => (frame.BeatSegments?.Quality ?? SignalQualityFlags.None)
           | SignalQualityFlagsMap.From(frame.SignalQuality);

    private string FormatThroughput() => string.Format(
        CultureInfo.InvariantCulture,
        "BG - FPS:{0}, SPS:{1}, SPF: {2} FG - FPS:{3}, SPS:{4}, SPF: {5}",
        _backgroundFps.ToString("F0", CultureInfo.InvariantCulture),
        _backgroundSps.ToString("F0", CultureInfo.InvariantCulture),
        _backgroundSpf.ToString("F0", CultureInfo.InvariantCulture),
        _foregroundFps.ToString("F0", CultureInfo.InvariantCulture),
        _foregroundSps.ToString("F0", CultureInfo.InvariantCulture),
        _foregroundSpf.ToString("F0", CultureInfo.InvariantCulture));
}
