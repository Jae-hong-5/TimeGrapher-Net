using System.Globalization;
using System.Text;

namespace TimeGrapher.App.Services;

internal sealed class UserErrorLog : IUserErrorLog
{
    private readonly string _path;
    private readonly string? _directory;
    private readonly Func<DateTimeOffset> _timestamp;
    private readonly object _gate = new();

    public UserErrorLog(string path, Func<DateTimeOffset>? timestamp = null)
    {
        _path = path;
        _directory = Path.GetDirectoryName(path);
        _timestamp = timestamp ?? (() => DateTimeOffset.Now);
    }

    public static string DefaultPath()
    {
        // Default to the per-user application-data location the app already uses for
        // settings (AppSettingsStore.FilePath: %AppData% on Windows,
        // $XDG_CONFIG_HOME/~/.config on Linux, ~/Library/Application Support on macOS),
        // not the install directory. A read-only / admin-owned install dir made the
        // first append throw and suppressed the user-facing error dialog.
        return BuildDefaultPath(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create));
    }

    internal static string BuildDefaultPath(string appDataRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            throw new InvalidOperationException("The per-user application data folder is not available.");
        }

        return Path.Combine(appDataRoot, "TimeGrapher", "log", "error.log");
    }

    public void Write(string userMessage, string detail)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        builder.Append(_timestamp().ToString("O", CultureInfo.InvariantCulture));
        builder.Append("] ");
        builder.AppendLine(userMessage);
        builder.AppendLine(detail);
        builder.AppendLine();

        lock (_gate)
        {
            if (!string.IsNullOrEmpty(_directory))
            {
                Directory.CreateDirectory(_directory);
            }

            File.AppendAllText(_path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
