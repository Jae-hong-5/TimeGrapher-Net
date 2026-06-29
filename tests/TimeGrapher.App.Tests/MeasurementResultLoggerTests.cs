using TimeGrapher.App.Services;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MeasurementResultLoggerTests
{
    [Fact]
    public void ObserveDisplayedWritesMeasurementCsvColumns()
    {
        string path = NewTempCsvPath();

        using (var logger = new MeasurementResultLogger(path, 54m))
        {
            logger.ObserveDisplayed(SampleFrame(sessionId: 7, sourceId: 42, version: 5));
        }

        string[] lines = File.ReadAllLines(path);
        File.Delete(path);

        Assert.Equal("lift_angle_deg,54.000000", lines[0]);
        Assert.Equal(
            "session_id,source_id,history_version,latest_time_s,bph,active_position," +
            "rate_valid,rate_s_per_day,amplitude_valid,amplitude_deg,beat_error_valid,beat_error_ms," +
            "diff_tic_tac_valid,diff_tic_tac_ms,diff_period_valid,diff_period_ms,avg_period_valid,avg_period_ms," +
            "rate_count,rate_mean_s_per_day,rate_sigma_s_per_day," +
            "amplitude_count,amplitude_mean_deg,amplitude_sigma_deg," +
            "missed_beat_detections,sync_loss_count",
            lines[1]);
        Assert.Equal(
            "7,42,5,12.500000,28800,CH,true,-2.500000,true,270.250000,true,0.125000," +
            "true,0.300000,true,-0.400000,true,0.500000,4,-1.000000,0.500000," +
            "3,268.000000,1.500000,6,2",
            lines[2]);
    }

    [Fact]
    public void ObserveDisplayedFlushesRowsBeforeDispose()
    {
        string path = NewTempCsvPath();
        const string expectedRow =
            "7,42,5,12.500000,28800,CH,true,-2.500000,true,270.250000,true,0.125000," +
            "true,0.300000,true,-0.400000,true,0.500000,4,-1.000000,0.500000," +
            "3,268.000000,1.500000,6,2";

        try
        {
            using (var logger = new MeasurementResultLogger(path, 54m))
            {
                logger.ObserveDisplayed(SampleFrame(sessionId: 7, sourceId: 42, version: 5));

                Assert.True(
                    SpinWait.SpinUntil(
                        () =>
                        {
                            string[] lines = ReadAllLinesShared(path);
                            return lines.Length >= 3 && lines[2] == expectedRow;
                        },
                        TimeSpan.FromSeconds(2)),
                    "Measurement rows must be readable before the logger is disposed.");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ObserveDisplayedSkipsDuplicateHistoryVersionInSameSession()
    {
        string path = NewTempCsvPath();

        using (var logger = new MeasurementResultLogger(path, 52m))
        {
            logger.ObserveDisplayed(SampleFrame(sessionId: 7, sourceId: 42, version: 5));
            logger.ObserveDisplayed(SampleFrame(sessionId: 7, sourceId: 43, version: 5));
            logger.ObserveDisplayed(SampleFrame(sessionId: 7, sourceId: 44, version: 6));
        }

        string[] lines = File.ReadAllLines(path);
        File.Delete(path);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("7,42,5,", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("7,44,6,", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void ObserveDisplayedLogsSameHistoryVersionInNewSession()
    {
        string path = NewTempCsvPath();

        using (var logger = new MeasurementResultLogger(path, 52m))
        {
            logger.ObserveDisplayed(SampleFrame(sessionId: 7, sourceId: 42, version: 5));
            logger.ObserveDisplayed(SampleFrame(sessionId: 8, sourceId: 1, version: 5));
        }

        string[] lines = File.ReadAllLines(path);
        File.Delete(path);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("7,42,5,", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("8,1,5,", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void ObserveDisplayedSkipsFramesWithoutMetricsHistory()
    {
        string path = NewTempCsvPath();

        using (var logger = new MeasurementResultLogger(path, 52m))
        {
            logger.ObserveDisplayed(new AnalysisFrame { SessionId = 7, SourceId = 42 });
        }

        string[] lines = File.ReadAllLines(path);
        File.Delete(path);

        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void ObserveDisplayedSkipsFramesBeforeMeasurementsAreValid()
    {
        string path = NewTempCsvPath();

        using (var logger = new MeasurementResultLogger(path, 52m))
        {
            logger.ObserveDisplayed(new AnalysisFrame
            {
                SessionId = 7,
                SourceId = 42,
                MetricsHistory = new BeatMetricsHistorySnapshot
                {
                    Version = 1,
                    LatestTimeS = 1.25,
                    Bph = 28800,
                },
            });
        }

        string[] lines = File.ReadAllLines(path);
        File.Delete(path);

        Assert.Equal(2, lines.Length);
    }

    private static AnalysisFrame SampleFrame(ulong sessionId, ulong sourceId, ulong version)
    {
        return new AnalysisFrame
        {
            SessionId = sessionId,
            SourceId = sourceId,
            MissedBeats = 6,
            SyncLossCount = 2,
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                Version = version,
                LatestTimeS = 12.5,
                Bph = 28800,
                ActivePosition = WatchPosition.CH,
                RateValid = true,
                RateSPerDay = -2.5,
                AmplitudeValid = true,
                AmplitudeDeg = 270.25,
                BeatErrorValid = true,
                BeatErrorSignedMs = 0.125,
                Derived = new DerivedTimingMeasures(
                    true,
                    0.3,
                    true,
                    -0.4,
                    true,
                    0.5),
                RateStats = new StatsSummary(
                    true,
                    -3.0,
                    1.0,
                    -1.0,
                    0.5,
                    4),
                AmplitudeStats = new StatsSummary(
                    true,
                    265.0,
                    272.0,
                    268.0,
                    1.5,
                    3),
            },
        };
    }

    private static string[] ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is string line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static string NewTempCsvPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
    }
}
