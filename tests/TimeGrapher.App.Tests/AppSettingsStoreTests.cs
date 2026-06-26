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
        AppSettingsStore.Flush();
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
                false,
                0.3,
                1.7,
                0.9),
            new SettingsWindowSettings(
                true,
                true,
                false,
                true,
                "180",
                true,
                7));

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

    [Fact]
    public void LoadFrom_InvalidRescueStrengthStep_ReturnsDefault()
    {
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        AppSettingsStore.SaveTo(path, AppSettings.Default with
        {
            SettingsWindow = AppSettings.Default.SettingsWindow with
            {
                WeakAOnsetRescueStrengthStep = WeakAOnsetRescueStrengthPolicy.MaxStep + 1,
            },
        });

        Assert.Equal(AppSettings.Default, AppSettingsStore.LoadFrom(path));
    }

    [Fact]
    public void LoadFrom_MissingNestedSetting_ReturnsDefault()
    {
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """
        {
          "Sampling": {
            "AnalysisBlockSize": 4096,
            "CaptureBufferMs": 20,
            "AveragingPeriod": 10
          },
          "AcceptBands": {
            "RateMinSPerDay": -4,
            "RateMaxSPerDay": 6,
            "AmplitudeMinDeg": 270,
            "AmplitudeMaxDeg": 300,
            "BeatErrorMagnitudeMs": 0.8
          },
          "LeftPanel": {
            "InputDeviceName": null,
            "SampleRate": 48000,
            "Gain": 100,
            "Bph": 0,
            "LiftAngle": 52,
            "SimulationBph": 28800,
            "SimulationErrorRate": 0,
            "SimulationAmplitude": 300,
            "SimulationBeatError": 0,
            "SimulationRealistic": true
          },
          "SettingsWindow": {
            "UseCOnset": false,
            "WeakAOnsetRescue": true,
            "PauseOnPositionChange": false,
            "HighPassCutoffText": "200",
            "MeasurementLogEnabled": false
          }
        }
        """);

        Assert.Equal(AppSettings.Default, AppSettingsStore.LoadFrom(path));
    }

    [Fact]
    public void LoadFrom_ConfigWithoutSignalScales_LoadsWithUnityDefaults()
    {
        // A settings file written before the A/B/C signal-size knobs existed (no
        // Simulation*Scale fields) must still load — the fields are optional — and each
        // cluster scale must default to 1.0 so an upgrade never silently mutes a cluster.
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """
        {
          "Sampling": {
            "AnalysisBlockSize": 4096,
            "CaptureBufferMs": 20,
            "AveragingPeriod": 10
          },
          "AcceptBands": {
            "RateMinSPerDay": -4,
            "RateMaxSPerDay": 6,
            "AmplitudeMinDeg": 270,
            "AmplitudeMaxDeg": 300,
            "BeatErrorMagnitudeMs": 0.8
          },
          "LeftPanel": {
            "InputDeviceName": null,
            "SampleRate": 48000,
            "Gain": 200,
            "Bph": 0,
            "LiftAngle": 52,
            "SimulationBph": 28800,
            "SimulationErrorRate": 0,
            "SimulationAmplitude": 300,
            "SimulationBeatError": 0,
            "SimulationRealistic": true
          },
          "SettingsWindow": {
            "UseCOnset": false,
            "WeakAOnsetRescue": true,
            "SpuriousBeatRejection": true,
            "PauseOnPositionChange": false,
            "HighPassCutoffText": "200",
            "MeasurementLogEnabled": false
          }
        }
        """);

        AppSettings loaded = AppSettingsStore.LoadFrom(path);

        // The file loaded (its distinctive Gain survived) rather than falling back to Default.
        Assert.Equal(200.0, loaded.LeftPanel.Gain);
        Assert.Equal(1.0, loaded.LeftPanel.SimulationSignalAScale);
        Assert.Equal(1.0, loaded.LeftPanel.SimulationSignalBScale);
        Assert.Equal(1.0, loaded.LeftPanel.SimulationSignalCScale);
        Assert.Equal(WeakAOnsetRescueStrengthPolicy.StandardStep, loaded.SettingsWindow.WeakAOnsetRescueStrengthStep);
    }

    [Fact]
    public void BuildFilePath_RejectsEmptyApplicationDataRoot()
    {
        Assert.Throws<InvalidOperationException>(() => AppSettingsStore.BuildFilePath(""));
    }

    [Fact]
    public void BuildFilePath_UsesOnlyTheApplicationDataRoot()
    {
        string root = Path.Combine(_directory, "config");

        string path = AppSettingsStore.BuildFilePath(root);

        Assert.Equal(Path.Combine(root, "TimeGrapher", "settings.json"), path);
    }

    [Fact]
    public void SaveQueuedTo_FlushWritesLatestSnapshot()
    {
        string path = Path.Combine(_directory, "settings.json");
        AppSettings first = AppSettings.Default with
        {
            LeftPanel = AppSettings.Default.LeftPanel with { Gain = 123.0 },
        };
        AppSettings latest = AppSettings.Default with
        {
            LeftPanel = AppSettings.Default.LeftPanel with { Gain = 456.0 },
        };

        AppSettingsStore.SaveQueuedTo(path, first);
        AppSettingsStore.SaveQueuedTo(path, latest);
        AppSettingsStore.Flush();

        Assert.Equal(latest, AppSettingsStore.LoadFrom(path));
    }

    [Fact]
    public void Flush_DoesNotThrow_WhenTheSaveFails()
    {
        // Flush is called from UI close handlers (and on the main window before its
        // workers and WAV recording are torn down), so a failed background save must be
        // swallowed, never rethrown. Here the target path is an existing directory, so
        // the write throws — Flush must still return cleanly.
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(path);

        AppSettingsStore.SaveQueuedTo(path, AppSettings.Default);

        AppSettingsStore.Flush();
    }
}
