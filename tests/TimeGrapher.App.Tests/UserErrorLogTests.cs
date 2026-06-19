using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class UserErrorLogTests
{
    [Fact]
    public void WriteAppendsTimestampedMessageAndDetail()
    {
        string directory = Path.Combine(Path.GetTempPath(), "timegrapher-error-log-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "error.log");
        try
        {
            var timestamp = new DateTimeOffset(2026, 6, 19, 13, 14, 15, TimeSpan.FromHours(-4));
            var logger = new UserErrorLog(path, () => timestamp);

            logger.Write(
                UserErrorMessages.CouldNotStartLiveAudio,
                "System.InvalidOperationException: device failed");

            string log = File.ReadAllText(path);
            Assert.Contains(
                "[2026-06-19T13:14:15.0000000-04:00] " +
                "We couldn't start live audio. Please check your device and try again.",
                log);
            Assert.Contains("System.InvalidOperationException: device failed", log);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
