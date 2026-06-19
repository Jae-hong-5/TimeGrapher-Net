using System.Diagnostics.CodeAnalysis;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class SoundPrintFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly SoundPrintRenderer _renderer;
    // Identity of the last frame whose sound image was taken. Dedup is keyed on
    // the frame, not the PixelBuffer, because the worker's 3-buffer publish pool
    // reuses a buffer reference after wraparound: a genuinely new publish can
    // carry the same PixelBuffer object as an earlier one, so reference-equality
    // would drop it. A re-routed kept frame (same instance) is still skipped, so
    // a theme-remapped _latestSoundImage is not overwritten by the raw original.
    private AnalysisFrame? _lastObservedFrame;
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
        _lastObservedFrame = null;
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
        if (TryRemapKeptImage(theme.ScopeBg, out PixelBuffer? remapped))
        {
            _renderer.RenderImage(remapped);
        }
    }

    // Internal seam: the remap state machine is testable without the blit,
    // which needs the Avalonia platform.
    internal bool TryRemapKeptImage(uint newBackground, [NotNullWhen(true)] out PixelBuffer? remapped)
    {
        uint oldBackground = _displayedBackground;
        _displayedBackground = newBackground;
        if (_latestSoundImage == null || oldBackground == newBackground)
        {
            remapped = null;
            return false;
        }

        remapped = RemapBackground(_latestSoundImage, oldBackground, newBackground);
        _latestSoundImage = remapped;
        return true;
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
        // Process each delivered sound-print publish once, keyed on the frame
        // object (not the pooled PixelBuffer, whose reference repeats after the
        // 3-buffer pool wraps). A re-routed kept frame is the same instance and
        // must not re-run, so a theme-remapped copy is not overwritten by the
        // old-background original; a new publish reusing a pooled buffer is a
        // different frame and must run.
        if (frame.SoundImageUpdated && frame.SoundImage != null &&
            !ReferenceEquals(frame, _lastObservedFrame))
        {
            _lastObservedFrame = frame;
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
        if (_latestSoundImage != null)
        {
            _renderer.RenderImage(_latestSoundImage);
        }
    }
}
