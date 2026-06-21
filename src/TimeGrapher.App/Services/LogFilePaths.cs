using System.IO;

namespace TimeGrapher.App.Services;

/// <summary>
/// Shared log-file path helpers. CLI --measurement-log/--analysis-log paths are opened directly
/// in the logger constructors at startup; create the parent directory first so a nested path
/// like out/run/foo.csv does not throw DirectoryNotFoundException and crash launch. A bare
/// filename has no parent.
/// </summary>
internal static class LogFilePaths
{
    public static string EnsureParentDirectory(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        return path;
    }
}
