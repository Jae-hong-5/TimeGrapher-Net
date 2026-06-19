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
        return Path.Combine(AppContext.BaseDirectory, "log", "error.log");
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
