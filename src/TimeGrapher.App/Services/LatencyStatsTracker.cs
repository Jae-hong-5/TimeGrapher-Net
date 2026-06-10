using System;
using System.Diagnostics;
using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Session aggregation of the QA latency evidence: capture-to-processing,
/// processing-to-display and end-to-end latency (average + worst case) plus
/// dropped-sample / coalesced-frame / missed-beat / sync-loss counts. Frames
/// carry Stopwatch timestamps stamped in Core; the display leg is stamped here
/// when the UI finishes rendering the frame.
/// </summary>
internal sealed class LatencyStatsTracker
{
    private const double StatusUpdateIntervalMs = 500.0;

    private readonly double _ticksPerMs;

    private long _frameCount;
    private double _capToProcSumMs;
    private double _capToProcMaxMs;
    private double _procToDispSumMs;
    private double _procToDispMaxMs;
    private double _endToEndSumMs;
    private double _endToEndMaxMs;
    private ulong _droppedSamples;
    private ulong _coalescedFrames;
    private ulong _missedBeats;
    private uint _syncLosses;
    private bool _sawLowerBoundCapture;
    private long _lastStatusTicks;

    public LatencyStatsTracker(double? ticksPerMs = null)
    {
        _ticksPerMs = ticksPerMs ?? Stopwatch.Frequency / 1000.0;
    }

    public double CapToProcAvgMs => _frameCount > 0 ? _capToProcSumMs / _frameCount : 0.0;
    public double CapToProcMaxMs => _capToProcMaxMs;
    public double ProcToDispAvgMs => _frameCount > 0 ? _procToDispSumMs / _frameCount : 0.0;
    public double ProcToDispMaxMs => _procToDispMaxMs;
    public double EndToEndAvgMs => _frameCount > 0 ? _endToEndSumMs / _frameCount : 0.0;
    public double EndToEndMaxMs => _endToEndMaxMs;
    public ulong DroppedSamples => _droppedSamples;
    public ulong CoalescedFrames => _coalescedFrames;
    public ulong MissedBeats => _missedBeats;
    public uint SyncLosses => _syncLosses;
    public long FrameCount => _frameCount;

    /// <summary>
    /// True when any observed capture stamp was a ring-eviction lower bound:
    /// the worst-case figures then under-report and the readout marks them "≥".
    /// </summary>
    public bool WorstCaseIsLowerBound => _sawLowerBoundCapture;

    public void Reset()
    {
        _frameCount = 0;
        _capToProcSumMs = _capToProcMaxMs = 0.0;
        _procToDispSumMs = _procToDispMaxMs = 0.0;
        _endToEndSumMs = _endToEndMaxMs = 0.0;
        _droppedSamples = 0;
        _coalescedFrames = 0;
        _missedBeats = 0;
        _syncLosses = 0;
        _sawLowerBoundCapture = false;
        _lastStatusTicks = 0;
    }

    /// <summary>Record one rendered frame. displayTicks = Stopwatch timestamp after render.</summary>
    public void Observe(AnalysisFrame frame, ulong coalescedFrames, long displayTicks)
    {
        if (frame.CaptureTimestamp > 0 && frame.ProcessingCompletedTimestamp > 0)
        {
            _sawLowerBoundCapture |= frame.CaptureTimestampIsLowerBound;
            double capToProc = (frame.ProcessingCompletedTimestamp - frame.CaptureTimestamp) / _ticksPerMs;
            double procToDisp = (displayTicks - frame.ProcessingCompletedTimestamp) / _ticksPerMs;
            double endToEnd = (displayTicks - frame.CaptureTimestamp) / _ticksPerMs;

            _frameCount++;
            _capToProcSumMs += capToProc;
            _capToProcMaxMs = Math.Max(_capToProcMaxMs, capToProc);
            _procToDispSumMs += procToDisp;
            _procToDispMaxMs = Math.Max(_procToDispMaxMs, procToDisp);
            _endToEndSumMs += endToEnd;
            _endToEndMaxMs = Math.Max(_endToEndMaxMs, endToEnd);
        }

        _droppedSamples += frame.InputSamplesDropped;
        _coalescedFrames += coalescedFrames;
        _missedBeats = frame.MissedBeats;
        _syncLosses = frame.SyncLossCount;
    }

    /// <summary>
    /// Status-bar text, refreshed at most every 500 ms so the readout does not
    /// flicker at frame rate. Returns null when no update is due yet.
    /// </summary>
    public string? TryFormatStatus(long nowTicks)
    {
        if (_frameCount == 0)
        {
            return null;
        }

        if (_lastStatusTicks != 0 && (nowTicks - _lastStatusTicks) / _ticksPerMs < StatusUpdateIntervalMs)
        {
            return null;
        }

        _lastStatusTicks = nowTicks;
        return FormatStatus();
    }

    public string FormatStatus() => string.Format(
        CultureInfo.InvariantCulture,
        "E2E {0:F0}/{10}{1:F0} ms (cap→proc {2:F0}/{10}{3:F0} + disp {4:F0}/{5:F0}) | drop {6} smp / {7} frm | miss {8} | sync−loss {9}",
        EndToEndAvgMs, EndToEndMaxMs,
        CapToProcAvgMs, CapToProcMaxMs,
        ProcToDispAvgMs, ProcToDispMaxMs,
        _droppedSamples, _coalescedFrames, _missedBeats, _syncLosses,
        _sawLowerBoundCapture ? "≥" : "");
}
