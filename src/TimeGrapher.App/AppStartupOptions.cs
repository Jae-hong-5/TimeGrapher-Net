namespace TimeGrapher.App;

internal sealed record AppStartupOptions(string? AnalysisLogPath, string? MeasurementLogPath)
{
    public static AppStartupOptions Current { get; private set; } = new((string?)null, (string?)null);

    public static void Configure(string[] args)
    {
        Current = Parse(args);
    }

    public static AppStartupOptions Parse(string[] args)
    {
        string? analysisLogPath = null;
        string? measurementLogPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--analysis-log" && i + 1 < args.Length)
            {
                analysisLogPath = args[++i];
            }
            else if (arg.StartsWith("--analysis-log=", StringComparison.Ordinal))
            {
                analysisLogPath = arg["--analysis-log=".Length..];
            }
            else if (arg == "--measurement-log" && i + 1 < args.Length)
            {
                measurementLogPath = args[++i];
            }
            else if (arg.StartsWith("--measurement-log=", StringComparison.Ordinal))
            {
                measurementLogPath = arg["--measurement-log=".Length..];
            }
        }

        return new AppStartupOptions(
            string.IsNullOrWhiteSpace(analysisLogPath) ? null : analysisLogPath,
            string.IsNullOrWhiteSpace(measurementLogPath) ? null : measurementLogPath);
    }
}
