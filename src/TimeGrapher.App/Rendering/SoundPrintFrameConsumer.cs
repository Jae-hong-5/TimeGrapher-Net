using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class SoundPrintFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly SoundPrintRenderer _renderer;
    private PixelBuffer? _latestSoundImage;
    private uint _displayedBackground = PlotThemePalette.Current.ScopeBg;

    public SoundPrintFrameConsumer(SoundPrintRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.SoundPrintTabId;

    internal PixelBuffer? LatestSoundImage => _latestSoundImage;

    public void Initialize(AnalysisTabResetContext context)
    {
        _ = context;
    }

    public void Reset(AnalysisTabResetContext context)
    {
        _ = context;
        _latestSoundImage = null;
        _renderer.Reset();
    }

    // After a stop there is no live worker to recolor and republish the sound
    // print (RunSessionController.SetSoundBackgroundColor is a no-op when
    // idle), so remap the kept image's background here. The worker fills the
    // background with exactly the palette ScopeBg, making the swap exact; the
    // remap writes into a copy because the published buffer belongs to the
    // worker's publish pool.
    public void ApplyTheme(PlotThemePalette theme)
    {
        uint oldBackground = _displayedBackground;
        _displayedBackground = theme.ScopeBg;
        if (_latestSoundImage == null || oldBackground == theme.ScopeBg)
        {
            return;
        }

        _latestSoundImage = RemapBackground(_latestSoundImage, oldBackground, theme.ScopeBg);
        _renderer.RenderImage(_latestSoundImage);
    }

    internal static PixelBuffer RemapBackground(PixelBuffer source, uint oldBackground, uint newBackground)
    {
        var recolored = new PixelBuffer(source.Width, source.Height);
        uint[] sourcePixels = source.Pixels;
        uint[] targetPixels = recolored.Pixels;
        for (int i = 0; i < sourcePixels.Length; i++)
        {
            uint pixel = sourcePixels[i];
            targetPixels[i] = pixel == oldBackground ? newBackground : pixel;
        }

        return recolored;
    }

    public void ObserveFrame(AnalysisFrame frame)
    {
        if (frame.SoundImageUpdated && frame.SoundImage != null)
        {
            _latestSoundImage = frame.SoundImage;
        }
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // The sound print is a Core-built pixel image (x = pixel columns of the
        // recent window, not stream time), so the review-cursor contract does
        // not apply; pause already freezes the image for inspection.
        _ = context;
        ObserveFrame(frame);
        if (frame.SoundImageUpdated && frame.SoundImage != null)
        {
            _renderer.RenderFrame(frame);
        }
        else if (_latestSoundImage != null)
        {
            _renderer.RenderImage(_latestSoundImage);
        }
    }
}
