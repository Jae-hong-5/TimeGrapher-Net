namespace TimeGrapher.App.Services;

internal interface IUserErrorLog
{
    void Write(string userMessage, string detail);
}

internal sealed class NullUserErrorLog : IUserErrorLog
{
    public static readonly IUserErrorLog Instance = new NullUserErrorLog();

    private NullUserErrorLog()
    {
    }

    public void Write(string userMessage, string detail)
    {
        _ = userMessage;
        _ = detail;
    }
}
