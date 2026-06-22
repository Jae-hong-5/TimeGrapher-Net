using Avalonia;
using TimeGrapher.App.Audio;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppStartupOptions.Configure(args);

        // Restore the user's saved accept-band limits before any graph is built so
        // every display opens against the persisted normal ranges (defaults if none).
        AcceptBandSettings.Current = AcceptBandSettingsStore.Load();

        // Restore the saved sampling parameters (analysis block size, capture buffer)
        // before the window is built so the Settings inputs open against the persisted
        // values (defaults if none). Applied at run start, not live.
        SamplingSettings.Current = SamplingSettingsStore.Load();

        if (args.Contains("--smoke", StringComparer.Ordinal))
        {
            _ = BuildAvaloniaApp();
            var app = new App();
            app.Initialize();
            _ = typeof(AnalysisFrame).Assembly.FullName;
            Console.WriteLine("TimeGrapher.App smoke OK");
            return 0;
        }

        if (args.Contains("--audio-smoke", StringComparer.Ordinal))
        {
            return AudioSmokeRunner.Run(args, capture: false);
        }

        if (args.Contains("--capture-smoke", StringComparer.Ordinal))
        {
            return AudioSmokeRunner.Run(args, capture: true);
        }

        if (args.Contains("--analysis-benchmark", StringComparer.Ordinal))
        {
            return AnalysisBenchmarkRunner.Run(args);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
