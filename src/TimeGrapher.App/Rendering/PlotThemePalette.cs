namespace TimeGrapher.App.Rendering;

internal readonly record struct PlotThemePalette(
    uint SurfaceBg,
    uint ScopeBg,
    uint ScopeGrid,
    uint TextPrimary,
    uint TraceTick,
    uint TraceTock,
    uint StatusError)
{
    public static PlotThemePalette Light { get; } = new(
        SurfaceBg: 0xFFF8F9FA,
        ScopeBg: 0xFFF4F6F8,
        ScopeGrid: 0xFFC8D0D8,
        TextPrimary: 0xFF1A1A1A,
        TraceTick: 0xFF2C9118,
        TraceTock: 0xFF1D8C9A,
        StatusError: 0xFFD22222);

    public static PlotThemePalette Dark { get; } = new(
        SurfaceBg: 0xFF0F1419,
        ScopeBg: 0xFF0F1419,
        ScopeGrid: 0xFF2A3441,
        TextPrimary: 0xFFE6EDF3,
        TraceTick: 0xFF5FDD45,
        TraceTock: 0xFF5FCEDD,
        StatusError: 0xFFFF5C5C);
}
