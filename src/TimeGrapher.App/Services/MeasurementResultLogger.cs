using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

internal sealed class MeasurementResultLogger : IMeasurementResultSink
{
    private const string Header =
        "session_id,source_id,history_version,latest_time_s,bph,active_position," +
        "rate_valid,rate_s_per_day,amplitude_valid,amplitude_deg,beat_error_valid,beat_error_ms," +
        "diff_tic_tac_valid,diff_tic_tac_ms,diff_period_valid,diff_period_ms,avg_period_valid,avg_period_ms," +
        "rate_count,rate_mean_s_per_day,rate_sigma_s_per_day," +
        "amplitude_count,amplitude_mean_deg,amplitude_sigma_deg," +
        "missed_beat_detections,sync_loss_count";

    private readonly BlockingCollection<MeasurementResultLogEntry> _entries = new();
    private readonly StreamWriter _writer;
    private readonly Thread _writerThread;
    private readonly object _gate = new();
    private ulong _lastSessionId;
    private ulong _lastHistoryVersion;
    private bool _haveLastHistory;
    private bool _disposed;

    public MeasurementResultLogger(string path)
    {
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _writer.WriteLine(Header);

        _writerThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "Measurement result logger",
        };
        _writerThread.Start();
    }

    public void ObserveDisplayed(AnalysisFrame frame)
    {
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        if (history == null)
        {
            return;
        }

        if (!HasMeasurementResult(history))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_haveLastHistory &&
                _lastSessionId == frame.SessionId &&
                _lastHistoryVersion == history.Version)
            {
                return;
            }

            _lastSessionId = frame.SessionId;
            _lastHistoryVersion = history.Version;
            _haveLastHistory = true;

            _entries.Add(MeasurementResultLogEntry.From(frame, history));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.CompleteAdding();
        }

        _writerThread.Join();
        _writer.Dispose();
        _entries.Dispose();
    }

    private void WriteLoop()
    {
        foreach (MeasurementResultLogEntry entry in _entries.GetConsumingEnumerable())
        {
            WriteEntry(entry);
        }

        _writer.Flush();
    }

    private static bool HasMeasurementResult(BeatMetricsHistorySnapshot history)
    {
        DerivedTimingMeasures derived = history.Derived;
        return history.RateValid ||
               history.AmplitudeValid ||
               history.BeatErrorValid ||
               derived.DiffTicTacValid ||
               derived.DiffPeriodValid ||
               derived.AvgPeriodValid;
    }

    private void WriteEntry(MeasurementResultLogEntry entry)
    {
        _writer.Write(entry.SessionId.ToString(CultureInfo.InvariantCulture));
        Write(entry.SourceId);
        Write(entry.HistoryVersion);
        Write(entry.LatestTimeS);
        Write(entry.Bph);
        Write(entry.ActivePosition);
        Write(entry.RateValid);
        Write(entry.RateSPerDay);
        Write(entry.AmplitudeValid);
        Write(entry.AmplitudeDeg);
        Write(entry.BeatErrorValid);
        Write(entry.BeatErrorMs);
        Write(entry.DiffTicTacValid);
        Write(entry.DiffTicTacMs);
        Write(entry.DiffPeriodValid);
        Write(entry.DiffPeriodMs);
        Write(entry.AvgPeriodValid);
        Write(entry.AvgPeriodMs);
        Write(entry.RateCount);
        Write(entry.RateMeanSPerDay);
        Write(entry.RateSigmaSPerDay);
        Write(entry.AmplitudeCount);
        Write(entry.AmplitudeMeanDeg);
        Write(entry.AmplitudeSigmaDeg);
        Write(entry.MissedBeatDetections);
        Write(entry.SyncLossCount);
        _writer.WriteLine();
    }

    private void Write(bool value)
    {
        _writer.Write(',');
        _writer.Write(value ? "true" : "false");
    }

    private void Write(ulong value)
    {
        _writer.Write(',');
        _writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    private void Write(long value)
    {
        _writer.Write(',');
        _writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    private void Write(int value)
    {
        _writer.Write(',');
        _writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    private void Write(double? value)
    {
        _writer.Write(',');
        if (value.HasValue)
        {
            _writer.Write(value.Value.ToString("F6", CultureInfo.InvariantCulture));
        }
    }

    private void Write(WatchPosition value)
    {
        _writer.Write(',');
        _writer.Write(value.ToString());
    }

    private readonly record struct MeasurementResultLogEntry(
        ulong SessionId,
        ulong SourceId,
        ulong HistoryVersion,
        double? LatestTimeS,
        int Bph,
        WatchPosition ActivePosition,
        bool RateValid,
        double? RateSPerDay,
        bool AmplitudeValid,
        double? AmplitudeDeg,
        bool BeatErrorValid,
        double? BeatErrorMs,
        bool DiffTicTacValid,
        double? DiffTicTacMs,
        bool DiffPeriodValid,
        double? DiffPeriodMs,
        bool AvgPeriodValid,
        double? AvgPeriodMs,
        long RateCount,
        double? RateMeanSPerDay,
        double? RateSigmaSPerDay,
        long AmplitudeCount,
        double? AmplitudeMeanDeg,
        double? AmplitudeSigmaDeg,
        ulong MissedBeatDetections,
        uint SyncLossCount)
    {
        public static MeasurementResultLogEntry From(AnalysisFrame frame, BeatMetricsHistorySnapshot history)
        {
            DerivedTimingMeasures derived = history.Derived;
            StatsSummary rateStats = history.RateStats;
            StatsSummary amplitudeStats = history.AmplitudeStats;
            return new MeasurementResultLogEntry(
                frame.SessionId,
                frame.SourceId,
                history.Version,
                history.LatestTimeS,
                history.Bph,
                history.ActivePosition,
                history.RateValid,
                history.RateValid ? history.RateSPerDay : null,
                history.AmplitudeValid,
                history.AmplitudeValid ? history.AmplitudeDeg : null,
                history.BeatErrorValid,
                history.BeatErrorValid ? history.BeatErrorSignedMs : null,
                derived.DiffTicTacValid,
                derived.DiffTicTacValid ? derived.DiffTicTacMs : null,
                derived.DiffPeriodValid,
                derived.DiffPeriodValid ? derived.DiffPeriodMs : null,
                derived.AvgPeriodValid,
                derived.AvgPeriodValid ? derived.AvgPeriodMs : null,
                rateStats.Count,
                rateStats.Valid ? rateStats.Mean : null,
                rateStats.Valid ? rateStats.Sigma : null,
                amplitudeStats.Count,
                amplitudeStats.Valid ? amplitudeStats.Mean : null,
                amplitudeStats.Valid ? amplitudeStats.Sigma : null,
                frame.MissedBeats,
                frame.SyncLossCount);
        }
    }
}
