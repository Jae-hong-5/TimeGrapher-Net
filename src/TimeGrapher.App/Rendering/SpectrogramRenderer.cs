using Avalonia.Controls;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Spectrogram tab: blits the Core-rendered STFT image (x = time,
/// y = frequency low-at-bottom, color = dB intensity) into its Image control —
/// the SoundPrintRenderer pattern — and draws the one-time dB color legend from
/// the same Core LUT the projector colors with, so the legend can never drift
/// from the image.
/// </summary>
internal sealed class SpectrogramRenderer
{
    private readonly Image _spectrogramImage;
    private readonly Image _legendImage;
    private bool _light = PlotThemePalette.Current.IsLight;

    public SpectrogramRenderer(Image spectrogramImage, Image legendImage)
    {
        _spectrogramImage = spectrogramImage;
        _legendImage = legendImage;
    }

    /// <summary>Builds the gradient legend bitmap from the active theme's colormap. UI thread only.</summary>
    public void InitializeLegend()
    {
        IReadOnlyList<uint> lut = SpectrogramFrameProjector.ColorLutFor(_light);
        var gradient = new PixelBuffer(lut.Count, 1);
        for (int x = 0; x < lut.Count; x++)
        {
            gradient.SetPixel(x, 0, lut[x]);
        }

        PixelBufferBitmap.UpdateImage(_legendImage, gradient);
    }

    /// <summary>
    /// Switches to the colormap for <paramref name="light"/> and rebuilds the
    /// legend so it matches; the next <see cref="Reset"/> uses the new floor color.
    /// The displayed image itself is recolored by the worker (live) or the
    /// consumer (stopped). UI thread only.
    /// </summary>
    public void ApplyTheme(bool light)
    {
        _light = light;
        InitializeLegend();
    }

    public void Reset()
    {
        // Always drop the previous run's image — with the tab hidden the bounds are
        // zero and the blank repaint below is skipped, which would leave stale data.
        _spectrogramImage.Source = null;

        int w = (int)_spectrogramImage.Bounds.Width;
        int h = (int)_spectrogramImage.Bounds.Height;
        if (w > 0 && h > 0)
        {
            var blank = new PixelBuffer(w, h);
            blank.Fill(SpectrogramFrameProjector.ColorLutFor(_light)[0]); // the dB-floor color
            PixelBufferBitmap.UpdateImage(_spectrogramImage, blank);
        }
    }

    public void RenderImage(PixelBuffer image)
    {
        PixelBufferBitmap.UpdateImage(_spectrogramImage, image);
    }
}
