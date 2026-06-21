using System;
using System.IO;
using TimeGrapher.App;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The accept-band store must round-trip edited limits and fall back to the
/// defaults — never throw — when the file is missing or unusable, since the bands
/// are display policy and a corrupt file must not block startup. Tests run against
/// a temp path so the real user-config file is never touched.
/// </summary>
public sealed class AcceptBandSettingsStoreTests : IDisposable
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
    public void SaveThenLoad_RoundTripsEditedLimits()
    {
        var saved = new AcceptBandSettings(-8.0, 6.0, 255.0, 305.0, 0.9);
        AcceptBandSettingsStore.SaveTo(_path, saved);

        AcceptBandSettings loaded = AcceptBandSettingsStore.LoadFrom(_path);

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefault()
    {
        Assert.Equal(AcceptBandSettings.Default, AcceptBandSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_MalformedFile_ReturnsDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not valid json");

        Assert.Equal(AcceptBandSettings.Default, AcceptBandSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_InvertedBand_FallsBackToDefault()
    {
        // A hand-edited file with min >= max is not a drawable band, so the loader
        // rejects it rather than feeding an inverted corridor to the graphs.
        AcceptBandSettingsStore.SaveTo(_path, new AcceptBandSettings(10.0, -10.0, 300.0, 270.0, 0.6));

        Assert.Equal(AcceptBandSettings.Default, AcceptBandSettingsStore.LoadFrom(_path));
    }

    [Fact]
    public void LoadFrom_OutOfRangeFiniteValue_FallsBackToDefault()
    {
        // A finite but out-of-range value (here beyond the decimal cast / UI range)
        // must not load, or seeding the NumericUpDown inputs would overflow at startup.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            "{\"RateMinSPerDay\":-10,\"RateMaxSPerDay\":1e29,\"AmplitudeMinDeg\":270,\"AmplitudeMaxDeg\":300,\"BeatErrorMagnitudeMs\":0.6}");

        Assert.Equal(AcceptBandSettings.Default, AcceptBandSettingsStore.LoadFrom(_path));
    }
}
