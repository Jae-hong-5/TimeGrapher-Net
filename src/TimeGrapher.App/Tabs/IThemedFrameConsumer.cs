using TimeGrapher.App.Rendering;

namespace TimeGrapher.App.Tabs;

/// <summary>
/// Frame consumers whose plots must recolor when the application theme toggles.
/// GraphFrameRenderer fans ApplyTheme out to every consumer implementing this,
/// so new plot tabs participate by implementing the interface instead of being
/// special-cased by concrete type.
/// </summary>
internal interface IThemedFrameConsumer
{
    void ApplyTheme(PlotThemePalette theme);
}
