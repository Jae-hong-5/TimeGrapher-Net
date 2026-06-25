using System.Text.Json;
using TimeGrapher.App.Rendering;

namespace TimeGrapher.App;

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly object SaveGate = new();
    private static QueuedSave? PendingSave;
    private static Task? SaveTask;

    public static string FilePath => BuildFilePath(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create));

    public static AppSettings Load() => LoadFrom(FilePath);

    public static void Save(AppSettings settings) => SaveQueuedTo(FilePath, settings);

    public static void SaveSampling(SamplingSettings sampling)
    {
        AppSettings.Current = AppSettings.Current with { Sampling = sampling };
        Save(AppSettings.Current);
    }

    public static void SaveAcceptBands(AcceptBandSettings acceptBands)
    {
        AppSettings.Current = AppSettings.Current with { AcceptBands = acceptBands };
        Save(AppSettings.Current);
    }

    internal static AppSettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return AppSettings.Default;
            }

            string text = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(text);
            if (!HasCompleteShape(document.RootElement))
            {
                return AppSettings.Default;
            }

            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(text);
            return loaded is { IsValid: true } ? loaded : AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    internal static void SaveTo(string path, AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }

    internal static void SaveQueuedTo(string path, AppSettings settings)
    {
        lock (SaveGate)
        {
            PendingSave = new QueuedSave(path, settings);
            SaveTask ??= Task.Run(DrainQueuedSaves);
        }
    }

    internal static void Flush()
    {
        // Settings are best-effort display/run policy. Flush is called from UI close
        // handlers (the Settings dialog Closed handler and main-window OnWindowClosed,
        // the latter before the input/analysis workers and the WAV recording are torn
        // down), so it must never throw: a failed save (read-only or full config dir,
        // locked file) used to crash the app on Settings-dialog close and abort
        // main-window teardown before those resources were released. Drain the queue
        // and let the background drain swallow any write failure, mirroring the prior
        // fire-and-forget sampling/accept-band stores.
        while (true)
        {
            Task? task;
            lock (SaveGate)
            {
                task = SaveTask;
            }

            if (task == null)
            {
                break;
            }

            task.GetAwaiter().GetResult();
        }
    }

    internal static string BuildFilePath(string appDataRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            throw new InvalidOperationException("The per-user application data folder is not available.");
        }

        return Path.Combine(appDataRoot, "TimeGrapher", "settings.json");
    }

    private static bool HasCompleteShape(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
            HasObject(root, nameof(AppSettings.Sampling), out JsonElement sampling) &&
            HasProperties(sampling,
                nameof(SamplingSettings.AnalysisBlockSize),
                nameof(SamplingSettings.CaptureBufferMs),
                nameof(SamplingSettings.AveragingPeriod)) &&
            HasObject(root, nameof(AppSettings.AcceptBands), out JsonElement acceptBands) &&
            HasProperties(acceptBands,
                nameof(AcceptBandSettings.RateMinSPerDay),
                nameof(AcceptBandSettings.RateMaxSPerDay),
                nameof(AcceptBandSettings.AmplitudeMinDeg),
                nameof(AcceptBandSettings.AmplitudeMaxDeg),
                nameof(AcceptBandSettings.BeatErrorMagnitudeMs)) &&
            HasObject(root, nameof(AppSettings.LeftPanel), out JsonElement leftPanel) &&
            HasProperties(leftPanel,
                nameof(LeftPanelSettings.InputDeviceName),
                nameof(LeftPanelSettings.SampleRate),
                nameof(LeftPanelSettings.Gain),
                nameof(LeftPanelSettings.Bph),
                nameof(LeftPanelSettings.LiftAngle),
                nameof(LeftPanelSettings.SimulationBph),
                nameof(LeftPanelSettings.SimulationErrorRate),
                nameof(LeftPanelSettings.SimulationAmplitude),
                nameof(LeftPanelSettings.SimulationBeatError),
                nameof(LeftPanelSettings.SimulationRealistic)) &&
            HasObject(root, nameof(AppSettings.SettingsWindow), out JsonElement settingsWindow) &&
            HasProperties(settingsWindow,
                nameof(SettingsWindowSettings.UseCOnset),
                nameof(SettingsWindowSettings.WeakAOnsetRescue),
                nameof(SettingsWindowSettings.SpuriousBeatRejection),
                nameof(SettingsWindowSettings.PauseOnPositionChange),
                nameof(SettingsWindowSettings.HighPassCutoffText),
                nameof(SettingsWindowSettings.MeasurementLogEnabled));
    }

    private static bool HasObject(JsonElement root, string propertyName, out JsonElement value)
    {
        return root.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static bool HasProperties(JsonElement root, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static void DrainQueuedSaves()
    {
        while (true)
        {
            QueuedSave save;
            lock (SaveGate)
            {
                if (PendingSave is not QueuedSave pending)
                {
                    SaveTask = null;
                    return;
                }

                save = pending;
                PendingSave = null;
            }

            try
            {
                SaveTo(save.Path, save.Settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort persistence: a write failure is intrinsic to file
                // persistence and must not surface as an unhandled exception (this
                // runs on a background task and Flush no longer rethrows). Drop it.
            }
        }
    }

    private readonly record struct QueuedSave(string Path, AppSettings Settings);
}
