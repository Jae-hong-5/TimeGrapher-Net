using Avalonia.Platform;
using ScottPlot;
using SkiaSharp;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Adapts the app's bundled Avalonia font assets to ScottPlot's independent
/// SkiaSharp font-resolution path.
/// </summary>
internal sealed class BundledPlotFontResolver : IFontResolver
{
    public const string FontFamily = "D2Coding";

    private static readonly Uri RegularFontUri =
        new("avares://TimeGrapher.App/Assets/Fonts/D2Coding/D2Coding.ttf");
    private static readonly Uri BoldFontUri =
        new("avares://TimeGrapher.App/Assets/Fonts/D2Coding/D2CodingBold.ttf");

    public SKTypeface? CreateTypeface(string fontName, bool bold, bool italic)
    {
        if (!string.Equals(fontName, FontFamily, StringComparison.Ordinal) || italic)
        {
            return null;
        }

        using Stream fontStream = AssetLoader.Open(bold ? BoldFontUri : RegularFontUri);
        return SKTypeface.FromStream(fontStream);
    }
}
