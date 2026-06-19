using TimeGrapher.App;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AppStartupOptionsTests
{
    [Fact]
    public void ParseReadsSeparateAndInlineAnalysisLogPath()
    {
        Assert.Equal("pi.csv", AppStartupOptions.Parse(
            new[] { "--analysis-log", "pi.csv" }).AnalysisLogPath);

        Assert.Equal("/tmp/pi.csv", AppStartupOptions.Parse(
            new[] { "--analysis-log=/tmp/pi.csv" }).AnalysisLogPath);
    }

    [Fact]
    public void ParseReadsSeparateAndInlineMeasurementLogPath()
    {
        Assert.Equal("measurements.csv", AppStartupOptions.Parse(
            new[] { "--measurement-log", "measurements.csv" }).MeasurementLogPath);

        Assert.Equal("/tmp/measurements.csv", AppStartupOptions.Parse(
            new[] { "--measurement-log=/tmp/measurements.csv" }).MeasurementLogPath);
    }

    [Fact]
    public void ParseIgnoresMissingOrBlankAnalysisLogPath()
    {
        Assert.Null(AppStartupOptions.Parse(
            new[] { "--analysis-log" }).AnalysisLogPath);

        Assert.Null(AppStartupOptions.Parse(
            new[] { "--analysis-log=" }).AnalysisLogPath);
    }

    [Fact]
    public void ParseIgnoresMissingOrBlankMeasurementLogPath()
    {
        Assert.Null(AppStartupOptions.Parse(
            new[] { "--measurement-log" }).MeasurementLogPath);

        Assert.Null(AppStartupOptions.Parse(
            new[] { "--measurement-log=" }).MeasurementLogPath);
    }
}
