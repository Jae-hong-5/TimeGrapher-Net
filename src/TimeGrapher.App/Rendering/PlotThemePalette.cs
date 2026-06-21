using Avalonia;
using Avalonia.Styling;
using AvaloniaColor = Avalonia.Media.Color;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Theme colors for the ScottPlot scopes. Values are NOT defined here — they are
/// read from the App.axaml ThemeDictionaries (the single source of truth) via
/// <see cref="FromResources"/>, so editing App.axaml recolors the graphs too.
/// </summary>
internal readonly record struct PlotThemePalette(
    uint SurfaceBg,
    uint ScopeBg,
    uint ScopeGrid,
    uint TextPrimary,
    uint TraceWave,
    uint TraceTick,
    uint TraceTock,
    uint TraceGhost = 0xFFA0A0A0,
    uint ChromeBorder = 0xFFCFCFCF,
    uint VarioAcceptBand = 0xFFE9C46A,
    uint VarioAcceptBandEdge = 0xFF9A6A00,
    uint VarioMinMax = 0xFF2D7DD2,
    uint VarioAverage = 0xFFC0392B,
    uint VarioGood = 0xFF0072B2,
    uint VarioWarn = 0xFFB06A00,
    uint VarioBad = 0xFFC03030)
{
    /// <summary>
    /// True when the scope background is light, so image-based tabs (the
    /// spectrogram) pick a light-background colormap. Derived from ScopeBg
    /// luminance so it tracks the App.axaml ScopeBgColor without a separate flag.
    /// </summary>
    public bool IsLight
    {
        get
        {
            byte r = (byte)(ScopeBg >> 16);
            byte g = (byte)(ScopeBg >> 8);
            byte b = (byte)ScopeBg;
            // Rec. 601 luma; brighter than mid-gray reads as a light background.
            return 0.299 * r + 0.587 * g + 0.114 * b > 127.5;
        }
    }

    /// <summary>Palette for the currently requested application theme variant.</summary>
    public static PlotThemePalette Current =>
        FromResources(Application.Current?.RequestedThemeVariant ?? ThemeVariant.Light);

    /// <summary>Builds the palette by reading the App.axaml color resources for <paramref name="theme"/>.</summary>
    public static PlotThemePalette FromResources(ThemeVariant theme) => new(
        SurfaceBg: Lookup("SurfaceColor", theme),
        ScopeBg: Lookup("ScopeBgColor", theme),
        ScopeGrid: Lookup("ScopeGridColor", theme),
        TextPrimary: Lookup("TextPrimaryColor", theme),
        TraceWave: Lookup("TraceWaveColor", theme),
        TraceTick: Lookup("TraceTickColor", theme),
        TraceTock: Lookup("TraceTockColor", theme),
        TraceGhost: Lookup("TraceGhostColor", theme),
        ChromeBorder: Lookup("ChromeBorderColor", theme),
        VarioAcceptBand: Lookup("VarioAcceptBandColor", theme),
        VarioAcceptBandEdge: Lookup("VarioAcceptBandEdgeColor", theme),
        VarioMinMax: Lookup("VarioMinMaxColor", theme),
        VarioAverage: Lookup("VarioAverageColor", theme),
        VarioGood: Lookup("VarioGoodColor", theme),
        VarioWarn: Lookup("VarioWarnColor", theme),
        VarioBad: Lookup("VarioBadColor", theme));

    private static uint Lookup(string key, ThemeVariant theme)
    {
        if (Application.Current is not { } app)
        {
            return 0xFF000000;
        }

        if (app.TryGetResource(key, theme, out object? value) && value is AvaloniaColor color)
        {
            return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }

        throw new InvalidOperationException($"Missing theme color resource '{key}'.");
    }
}
