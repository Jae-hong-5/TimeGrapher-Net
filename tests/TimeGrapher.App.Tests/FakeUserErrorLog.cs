using TimeGrapher.App.Services;

namespace TimeGrapher.App.Tests;

internal sealed class FakeUserErrorLog : IUserErrorLog
{
    public List<(string UserMessage, string Detail)> Entries { get; } = new();

    public void Write(string userMessage, string detail)
    {
        Entries.Add((userMessage, detail));
    }
}
