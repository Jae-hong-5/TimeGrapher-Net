using System.Diagnostics.CodeAnalysis;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class SpectrogramFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly SpectrogramRenderer _renderer;
    // The worker buffer that produced the displayed image. A theme toggle
    // replaces _latestSpectrogramImage with a remapped copy while this keeps
    // pointing at the original, so a re-routed kept frame (same pooled buffer)
    // is recognized and cannot restore the old-colormap image.
    private PixelBuffer? _latestSourceImage;
    private PixelBuffer? _latestSpectrogramImage;
    private bool _displayedLight = PlotThemePalette.Current.IsLight;

    public SpectrogramFrameConsumer(SpectrogramRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.SpectrogramTabId;

    internal PixelBuffer? LatestSpectrogramImage => _latestSpectrogramImage;

    public void Initialize(AnalysisTabResetContext context)
    {
        _ = context;
        _renderer.InitializeLegend();
    }

    public void Reset(AnalysisTabResetContext context)
    {
        _ = context;
        _latestSourceImage = null;
        _latestSpectrogramImage = null;
        _renderer.Reset();
    }

    // Rebuild the legend for the new theme, then recolor the displayed image.
    // While running the worker recolors and republishes; but after a stop there
    // is no live worker (RunSessionController.SetSpectrogramColormap is a no-op
    // when idle), so mirror the kept image's colormap here. The remap writes
    // into a copy because the published buffer belongs to the worker's pool.
    public void ApplyTheme(PlotThemePalette theme)
    {
        _renderer.ApplyTheme(theme.IsLight);
        if (TryRemapKeptImage(theme.IsLight, out PixelBuffer? remapped))
        {
            _renderer.RenderImage(remapped);
        }
    }

    // Internal seam: the remap state machine is testable without the blit,
    // which needs the Avalonia platform.
    internal bool TryRemapKeptImage(bool light, [NotNullWhen(true)] out PixelBuffer? remapped)
    {
        bool changed = light != _displayedLight;
        _displayedLight = light;
        if (_latestSpectrogramImage == null || !changed)
        {
            remapped = null;
            return false;
        }

        remapped = MirrorColormap(_latestSpectrogramImage, light);
        _latestSpectrogramImage = remapped;
        return true;
    }

    internal static PixelBuffer MirrorColormap(PixelBuffer source, bool toLight)
    {
        var recolored = new PixelBuffer(source.Width, source.Height);
        Array.Copy(source.Pixels, recolored.Pixels, source.Pixels.Length);
        SpectrogramFrameProjector.MirrorColormap(recolored.Pixels, toLight);
        return recolored;
    }

    public void ObserveFrame(AnalysisFrame frame)
    {
        // A re-routed kept frame carries the same pooled buffer reference (the
        // publish pool never repeats a reference on consecutive publishes), so
        // re-observing it must not overwrite a theme-remapped copy with the
        // old-colormap original.
        if (frame.SpectrogramImageUpdated && frame.SpectrogramImage != null &&
            !ReferenceEquals(frame.SpectrogramImage, _latestSourceImage))
        {
            _latestSourceImage = frame.SpectrogramImage;
            _latestSpectrogramImage = frame.SpectrogramImage;
        }
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // The spectrogram is a Core-built pixel image (x = pixel columns of the
        // recent window, not stream time), so the review-cursor contract does
        // not apply; pause already freezes the image for inspection.
        _ = context;
        ObserveFrame(frame);
        if (_latestSpectrogramImage != null)
        {
            _renderer.RenderImage(_latestSpectrogramImage);
        }
    }
}
