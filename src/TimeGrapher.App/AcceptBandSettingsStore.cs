using System.Text.Json;
using TimeGrapher.App.Rendering;

namespace TimeGrapher.App;

/// <summary>
/// Loads and saves the user's acceptable-band limits as JSON under the per-user
/// application-data folder, so the limits a grader sets survive an app restart.
/// Settings live in the OS user-config location (roaming AppData on Windows,
/// ~/.config on Linux/Raspberry Pi) rather than the executable's log folder, which
/// may be read-only on a packaged install. A missing or malformed file falls back
/// to <see cref="AcceptBandSettings.Default"/> instead of throwing: the bands are
/// display policy, so a corrupt or hand-edited file must never block startup
/// (fallback is intrinsic to file persistence, not extra defensive logic).
/// </summary>
internal static class AcceptBandSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGrapher",
        "accept-bands.json");

    /// <summary>Returns the persisted limits, or the defaults when none are stored or the file is unusable.</summary>
    public static AcceptBandSettings Load() => LoadFrom(FilePath);

    /// <summary>Persists the limits, creating the settings folder on first save.</summary>
    public static void Save(AcceptBandSettings settings) => SaveTo(FilePath, settings);

    // Path-parameterized cores so the round-trip and fallback behaviour are
    // testable against a temp file instead of the real user-config location.
    internal static AcceptBandSettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
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

    internal static void SaveTo(string path, AcceptBandSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }
}
