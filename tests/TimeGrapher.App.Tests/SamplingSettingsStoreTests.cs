using System;
using System.IO;
using TimeGrapher.App;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The sampling store must round-trip edited parameters and fall back to the defaults —
/// never throw — when the file is missing or unusable, so a corrupt or hand-edited file
/// cannot block startup. Tests run against a temp path so the real user-config file is
/// never touched. Mirrors <see cref="AcceptBandSettingsStoreTests"/>.
/// </summary>
public sealed class SamplingSettingsStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "TimeGrapherTests", Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEditedParameters()
    {
        var saved = new SamplingSettings(AnalysisBlockSize: 8192, CaptureBufferMs: 50, AveragingPeriod: 45);
        SamplingSettingsStore.SaveTo(_path, saved);

        Assert.Equal(saved, SamplingSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefault()
    {
        Assert.Equal(SamplingSettings.Default, SamplingSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_MalformedFile_ReturnsDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not valid json");

        Assert.Equal(SamplingSettings.Default, SamplingSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_FileWithoutAveragingPeriod_UsesDefaultAveragingPeriod()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"AnalysisBlockSize\":8192,\"CaptureBufferMs\":50}");

        Assert.Equal(new SamplingSettings(8192, 50, 10), SamplingSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_OutOfRangeValue_FallsBackToDefault()
    {
        SamplingSettingsStore.SaveTo(_path, new SamplingSettings(AnalysisBlockSize: 16, CaptureBufferMs: 20, AveragingPeriod: 20));

        Assert.Equal(SamplingSettings.Default, SamplingSettingsStore.LoadFrom(_path));
    }

    [Theory]
    [InlineData(257, 20, 20)]
    [InlineData(4096, 6, 20)]
    [InlineData(4096, 20, 0)]
    [InlineData(4096, 20, 241)]
    public void LoadFrom_InvalidValue_FallsBackToDefault(int block, int buffer, int averagingPeriod)
    {
        SamplingSettingsStore.SaveTo(_path, new SamplingSettings(block, buffer, averagingPeriod));

        Assert.Equal(SamplingSettings.Default, SamplingSettingsStore.LoadFrom(_path));
    }

    [Theory]
    [InlineData(256, 5, 1)]
    [InlineData(16384, 200, 240)]
    public void SaveThenLoad_RoundTripsBoundaryValues(int block, int buffer, int averagingPeriod)
    {
        var saved = new SamplingSettings(block, buffer, averagingPeriod);
        SamplingSettingsStore.SaveTo(_path, saved);

        Assert.Equal(saved, SamplingSettingsStore.LoadFrom(_path));
    }
}
