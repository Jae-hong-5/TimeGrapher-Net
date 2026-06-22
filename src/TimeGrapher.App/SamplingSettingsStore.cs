using System.Text.Json;

namespace TimeGrapher.App;

/// <summary>
/// Loads and saves the user's sampling parameters (analysis block size, capture buffer
/// length) as JSON under the per-user application-data folder, so a tuned value survives
/// an app restart. Settings live in the OS user-config location (roaming AppData on
/// Windows, ~/.config on Linux/Raspberry Pi) rather than the executable's log folder,
/// which may be read-only on a packaged install. A missing or malformed file falls back
/// to <see cref="SamplingSettings.Default"/> instead of throwing: a corrupt or hand-edited
/// file must never block startup (fallback is intrinsic to file persistence, not extra
/// defensive logic). Mirrors <see cref="AcceptBandSettingsStore"/>.
/// </summary>
internal static class SamplingSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGrapher",
        "sampling.json");

    /// <summary>Returns the persisted parameters, or the defaults when none are stored or the file is unusable.</summary>
    public static SamplingSettings Load() => LoadFrom(FilePath);

    /// <summary>Persists the parameters, creating the settings folder on first save.</summary>
    public static void Save(SamplingSettings settings) => SaveTo(FilePath, settings);

    // Path-parameterized cores so the round-trip and fallback behaviour are testable
    // against a temp file instead of the real user-config location.
    internal static SamplingSettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
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

    internal static void SaveTo(string path, SamplingSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }
}
