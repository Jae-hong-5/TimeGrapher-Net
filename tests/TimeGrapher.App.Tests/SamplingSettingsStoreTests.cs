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
        var saved = new SamplingSettings(AnalysisBlockSize: 8192, CaptureBufferMs: 50);
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
    public void LoadFrom_OutOfRangeValue_FallsBackToDefault()
    {
        // A block size below the editable floor is not a usable window, so the loader
        // rejects it rather than seeding the NumericUpDown with a value the UI cannot
        // represent or that would starve the analysis pass.
        SamplingSettingsStore.SaveTo(_path, new SamplingSettings(AnalysisBlockSize: 16, CaptureBufferMs: 20));

        Assert.Equal(SamplingSettings.Default, SamplingSettingsStore.LoadFrom(_path));
    }

    [Theory]
    [InlineData(257, 20)]   // block off the 256-step grid
    [InlineData(4096, 6)]   // buffer off the 5-step grid
    public void LoadFrom_OffStepValue_FallsBackToDefault(int block, int buffer)
    {
        // An off-step value is not UI-representable on the NumericUpDown grid, so the
        // loader rejects it the same way it rejects an out-of-range value.
        SamplingSettingsStore.SaveTo(_path, new SamplingSettings(block, buffer));

        Assert.Equal(SamplingSettings.Default, SamplingSettingsStore.LoadFrom(_path));
    }

    [Theory]
    [InlineData(256, 5)]        // floors
    [InlineData(16384, 200)]    // ceilings
    public void SaveThenLoad_RoundTripsBoundaryValues(int block, int buffer)
    {
        var saved = new SamplingSettings(block, buffer);
        SamplingSettingsStore.SaveTo(_path, saved);

        Assert.Equal(saved, SamplingSettingsStore.LoadFrom(_path));
    }
}
