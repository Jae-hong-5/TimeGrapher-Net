using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
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

        AppSettings.Current = AppSettingsStore.Load();
        AcceptBandSettings.Current = AppSettings.Current.AcceptBands;
        SamplingSettings.Current = AppSettings.Current.Sampling;

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
    {
        // ScottPlot has a process-wide font cache that is independent from
        // Avalonia's font manager. Register the bundled graph font before any
        // AvaPlot can be constructed or rendered, otherwise a missing Linux
        // system font can be cached under the D2Coding name for the process.
        PlotThemeHelper.ConfigureFonts();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The AI analysis window and tab headers use the Latin/code-oriented Hack
            // face, which has no Hangul (and limited non-Latin) glyphs. Register the
            // bundled D2Coding family - the app body font, which covers Hangul - as an
            // explicit fallback so missing glyphs resolve to a shipped font instead of
            // relying on ambient system fallback (tofu on the minimal Linux kiosk target,
            // where a covering system font may be absent). D2Coding ships with the app,
            // so the fallback is deterministic across Windows and the Raspberry Pi build.
            .With(new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new FontFallback
                    {
                        FontFamily = new FontFamily("avares://TimeGrapher.App/Assets/Fonts/D2Coding#D2Coding"),
                    },
                },
            })
            .LogToTrace();
    }
}
