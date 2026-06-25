using System;
using System.IO;
using TimeGrapher.App;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TimeGrapherTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllSettings()
    {
        string path = Path.Combine(_directory, "settings.json");
        var saved = new AppSettings(
            new SamplingSettings(8192, 50, 45),
            new AcceptBandSettings(-8.0, 6.0, 255.0, 305.0, 0.9),
            new LeftPanelSettings(
                "Live: Welshi USB",
                96000,
                420.0,
                21600,
                54.0,
                18000,
                -12.0,
                280.0,
                0.5,
                false),
            new SettingsWindowSettings(
                true,
                true,
                true,
                "180",
                true));

        AppSettingsStore.SaveTo(path, saved);

        Assert.Equal(saved, AppSettingsStore.LoadFrom(path));
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefault()
    {
        Assert.Equal(AppSettings.Default, AppSettingsStore.LoadFrom(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public void LoadFrom_MalformedFile_ReturnsDefault()
    {
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ this is not valid json");

        Assert.Equal(AppSettings.Default, AppSettingsStore.LoadFrom(path));
    }

    [Fact]
    public void LoadFrom_InvalidNestedValue_ReturnsDefault()
    {
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        AppSettingsStore.SaveTo(path, AppSettings.Default with
        {
            Sampling = new SamplingSettings(AnalysisBlockSize: 16, CaptureBufferMs: 20, AveragingPeriod: 20),
        });

        Assert.Equal(AppSettings.Default, AppSettingsStore.LoadFrom(path));
    }
}
