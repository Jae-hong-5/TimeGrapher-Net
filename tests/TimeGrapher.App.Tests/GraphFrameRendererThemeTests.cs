using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// GraphFrameRenderer.ApplyTheme must fan out to exactly the consumers that opt in
/// via IThemedFrameConsumer (passing the palette through), and never touch plain
/// consumers — the same broadcast contract as the accept-band fan-out, so a new
/// plot tab recolors by implementing the interface rather than being special-cased.
/// </summary>
public sealed class GraphFrameRendererThemeTests
{
    private sealed class ThemedConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
    {
        public int ApplyCalls { get; private set; }
        public PlotThemePalette? LastTheme { get; private set; }
        public string TabId => "themed";
        public void Initialize(AnalysisTabResetContext context) { }
        public void Reset(AnalysisTabResetContext context) { }
        public void ObserveFrame(AnalysisFrame frame) { }
        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context) { }
        public void ApplyTheme(PlotThemePalette theme)
        {
            LastTheme = theme;
            ApplyCalls++;
        }
    }

    private sealed class PlainConsumer : IAnalysisFrameConsumer
    {
        public string TabId => "plain";
        public void Initialize(AnalysisTabResetContext context) { }
        public void Reset(AnalysisTabResetContext context) { }
        public void ObserveFrame(AnalysisFrame frame) { }
        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context) { }
    }

    [Fact]
    public void ApplyTheme_InvokesOnlyThemedConsumers_WithThePalette()
    {
        var themed1 = new ThemedConsumer();
        var themed2 = new ThemedConsumer();
        var plain = new PlainConsumer();
        var renderer = new GraphFrameRenderer(
            new IAnalysisFrameConsumer[] { themed1, plain, themed2 }, new TextBlock());
        var palette = new PlotThemePalette(
            SurfaceBg: 0xFF101010, ScopeBg: 0xFF202020, ScopeGrid: 0xFF303030,
            TextPrimary: 0xFF404040, TraceWave: 0xFF505050, TraceTick: 0xFF116611,
            TraceTock: 0xFF606060, VarioMinMax: 0xFF2266CC, VarioBad: 0xFFCC2233);

        renderer.ApplyTheme(palette);

        // Themed consumers are fanned out to exactly once, with the same palette.
        Assert.Equal(1, themed1.ApplyCalls);
        Assert.Equal(1, themed2.ApplyCalls);
        Assert.True(themed1.LastTheme.HasValue);
        Assert.Equal(palette, themed1.LastTheme!.Value);
        Assert.Equal(palette, themed2.LastTheme!.Value);
        // PlainConsumer has no ApplyTheme; reaching here without throwing confirms
        // the OfType<IThemedFrameConsumer> fan-out excluded it.
    }
}
