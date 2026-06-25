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

    public static AppSettings Load() => LoadFrom(FilePath);

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

    internal static AppSettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return AppSettings.Default;
            }

            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
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
}
