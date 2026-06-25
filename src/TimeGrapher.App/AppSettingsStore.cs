using System.Text.Json;
using TimeGrapher.App.Rendering;

namespace TimeGrapher.App;

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGrapher",
        "settings.json");

    private static string LegacySamplingPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGrapher",
        "sampling.json");

    private static string LegacyAcceptBandsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGrapher",
        "accept-bands.json");

    public static AppSettings Load() => LoadFrom(FilePath, LegacySamplingPath, LegacyAcceptBandsPath);

    public static void Save(AppSettings settings) => SaveTo(FilePath, settings);

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

    internal static AppSettings LoadFrom(string path, string? legacySamplingPath = null, string? legacyAcceptBandsPath = null)
    {
        try
        {
            if (File.Exists(path))
            {
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                return loaded is { IsValid: true } ? loaded : AppSettings.Default;
            }

            SamplingSettings sampling = LoadLegacySampling(legacySamplingPath);
            AcceptBandSettings acceptBands = LoadLegacyAcceptBands(legacyAcceptBandsPath);
            return AppSettings.Default with
            {
                Sampling = sampling,
                AcceptBands = acceptBands,
            };
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

    private static SamplingSettings LoadLegacySampling(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path))
            {
                return SamplingSettings.Default;
            }

            SamplingSettings? loaded = JsonSerializer.Deserialize<SamplingSettings>(File.ReadAllText(path));
            return loaded is { IsValid: true } ? loaded : SamplingSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return SamplingSettings.Default;
        }
    }

    private static AcceptBandSettings LoadLegacyAcceptBands(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path))
            {
                return AcceptBandSettings.Default;
            }

            AcceptBandSettings? loaded = JsonSerializer.Deserialize<AcceptBandSettings>(File.ReadAllText(path));
            return loaded is { IsValid: true } ? loaded : AcceptBandSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AcceptBandSettings.Default;
        }
    }
}
