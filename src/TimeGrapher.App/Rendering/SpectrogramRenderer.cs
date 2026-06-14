using Avalonia.Controls;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>Spectrogram time-window selector mode (the Qt original's Last Beat / Seconds).</summary>
internal enum SpectrogramViewMode
{
    LastBeat,
    Seconds,
}

/// <summary>
/// Spectrogram tab: paints the Core-rendered STFT columns into an oscilloscope
/// sweep buffer — filling left to right and wrapping back to the left to
/// overwrite when the window is full — and blits it into its Image control. The
/// window width is the user-selected Last Beat (one beat period) or a fixed
/// number of Seconds; the time axis re-labels to match. The dB color legend is
/// drawn from the same Core LUT the projector colors with, so it can never drift
/// from the image.
/// </summary>
internal sealed class SpectrogramRenderer
{
    /// <summary>Beat period assumed before the detector locks (28800 bph), for Last Beat.</summary>
    private const double FallbackBeatPeriodS = 0.125;

    private readonly Image _spectrogramImage;
    private readonly Image _legendImage;
    private readonly TextBlock[] _timeLabels;
    private readonly TextBlock _timeCaption;

    private bool _light = PlotThemePalette.Current.IsLight;
    private SpectrogramViewMode _viewMode = SpectrogramViewMode.Seconds;
    private double _viewSeconds = 1.0;

    // Latest published image and its windowing metadata, kept so a selector change
    // re-seeds the sweep without waiting for the next frame.
    private PixelBuffer? _lastImage;
    private int _lastLiveColumn;
    private double _lastColumnSeconds;
    private double _lastBeatPeriodS;

    // Oscilloscope sweep buffer (the displayed image): reallocated only on a
    // window-size change. _sweepHead is the next column to (over)write; it advances
    // left to right and wraps to 0. _lastSeenLiveColumn tracks how far the source
    // wrap buffer had been written, so each publish appends only its new columns.
    private PixelBuffer? _sweepBuffer;
    private int _sweepCols = -1;
    private int _sweepHead;
    private int _lastSeenLiveColumn = -1;

    // The scope-background color the empty (no-input) region is painted with, kept
    // so a theme toggle can recolor just those pixels (the colormap is theme-agnostic).
    private uint _emptyColor = PlotThemePalette.Current.ScopeBg;

    public SpectrogramRenderer(
        Image spectrogramImage,
        Image legendImage,
        TextBlock[] timeLabels,
        TextBlock timeCaption)
    {
        _spectrogramImage = spectrogramImage;
        _legendImage = legendImage;
        _timeLabels = timeLabels;
        _timeCaption = timeCaption;
        UpdateTimeAxis(CurrentWindowSeconds());
    }

    /// <summary>
    /// Builds the vertical dB colorbar bitmap from the colormap: top = dB ceiling
    /// (peak, high LUT index), bottom = the dB floor. UI thread only.
    /// </summary>
    public void InitializeLegend()
    {
        IReadOnlyList<uint> lut = SpectrogramFrameProjector.ColorLutFor(_light);
        var gradient = new PixelBuffer(1, lut.Count);
        for (int y = 0; y < lut.Count; y++)
        {
            gradient.SetPixel(0, y, lut[lut.Count - 1 - y]);
        }

        PixelBufferBitmap.UpdateImage(_legendImage, gradient);
    }

    /// <summary>
    /// Recolors the empty (no-input) region to the new scope background — the
    /// colormap itself is theme-agnostic, so only the background follows the
    /// theme — and rebuilds the legend. UI thread only.
    /// </summary>
    public void ApplyTheme(bool light)
    {
        _light = light;
        uint newEmpty = PlotThemePalette.Current.ScopeBg;
        if (_sweepBuffer != null && newEmpty != _emptyColor)
        {
            uint[] pixels = _sweepBuffer.Pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] == _emptyColor)
                {
                    pixels[i] = newEmpty;
                }
            }

            PixelBufferBitmap.UpdateImage(_spectrogramImage, _sweepBuffer);
        }

        _emptyColor = newEmpty;
        InitializeLegend();
    }

    /// <summary>Selects the time-window mode and re-seeds the sweep. UI thread only.</summary>
    public void SetViewMode(SpectrogramViewMode mode)
    {
        _viewMode = mode;
        RenderCurrent(reseed: true);
    }

    /// <summary>Sets the Seconds-mode window length and re-seeds the sweep. UI thread only.</summary>
    public void SetViewSeconds(double seconds)
    {
        _viewSeconds = seconds;
        RenderCurrent(reseed: true);
    }

    public void Reset()
    {
        _lastImage = null;
        _sweepBuffer = null; // drop the previous run's swept content so the next run seeds fresh
        _sweepCols = -1;
        _sweepHead = 0;
        _lastSeenLiveColumn = -1;

        // Always drop the previous run's image — with the tab hidden the bounds are
        // zero and the blank repaint below is skipped, which would leave stale data.
        _spectrogramImage.Source = null;

        int w = (int)_spectrogramImage.Bounds.Width;
        int h = (int)_spectrogramImage.Bounds.Height;
        if (w > 0 && h > 0)
        {
            var blank = new PixelBuffer(w, h);
            blank.Fill(PlotThemePalette.Current.ScopeBg); // empty = scope background, not the dB-floor canvas
            PixelBufferBitmap.UpdateImage(_spectrogramImage, blank);
        }
    }

    /// <summary>
    /// Stores the latest published image plus its windowing metadata and appends
    /// its new columns to the sweep. UI thread only.
    /// </summary>
    public void RenderWindowed(PixelBuffer image, int liveColumn, double columnSeconds, double beatPeriodS)
    {
        _lastImage = image;
        _lastLiveColumn = liveColumn;
        _lastColumnSeconds = columnSeconds;
        _lastBeatPeriodS = beatPeriodS;
        RenderCurrent(reseed: false);
    }

    private void RenderCurrent(bool reseed)
    {
        double windowSeconds = CurrentWindowSeconds();
        if (_lastImage == null || _lastColumnSeconds <= 0.0)
        {
            UpdateTimeAxis(windowSeconds);
            return;
        }

        int sourceWidth = _lastImage.Width;
        int height = _lastImage.Height;
        int cols = (int)Math.Round(windowSeconds / _lastColumnSeconds);
        cols = Math.Clamp(cols, 1, sourceWidth);

        if (reseed || _sweepBuffer == null || _sweepCols != cols || _sweepBuffer.Height != height)
        {
            SeedSweep(height, cols);
        }
        else if (!AppendNewColumns(sourceWidth, height, cols))
        {
            return; // nothing new since the last render
        }

        PixelBufferBitmap.UpdateImage(_spectrogramImage, _sweepBuffer!);
        UpdateTimeAxis(cols * _lastColumnSeconds); // label the window actually shown
    }

    // (Re)start the sweep: paint the whole window with the scope BACKGROUND color
    // so the not-yet-reached part reads as empty (no input received), distinct
    // from the dB-floor canvas color that means "received, but quiet". The write
    // head starts at the left and real data fills in from there at the live rate —
    // only as much as has actually elapsed.
    private void SeedSweep(int height, int cols)
    {
        if (_sweepBuffer == null || _sweepBuffer.Width != cols || _sweepBuffer.Height != height)
        {
            _sweepBuffer = new PixelBuffer(cols, height);
        }

        _emptyColor = PlotThemePalette.Current.ScopeBg;
        _sweepBuffer.Fill(_emptyColor);
        _sweepCols = cols;
        _sweepHead = 0;
        _lastSeenLiveColumn = _lastLiveColumn;
    }

    // Append the columns the source wrote since the last render at the sweep head,
    // advancing it left to right and wrapping to 0. Returns false when nothing is new.
    private bool AppendNewColumns(int sourceWidth, int height, int cols)
    {
        int newCount = ((_lastLiveColumn - _lastSeenLiveColumn) % sourceWidth + sourceWidth) % sourceWidth;
        if (newCount == 0)
        {
            return false;
        }

        int startColumn = _lastSeenLiveColumn;
        if (newCount > cols)
        {
            // Fell behind by more than a full sweep; only the last `cols` matter.
            startColumn = ((_lastLiveColumn - cols) % sourceWidth + sourceWidth) % sourceWidth;
            newCount = cols;
        }

        uint[] source = _lastImage!.Pixels;
        uint[] target = _sweepBuffer!.Pixels;
        for (int j = 0; j < newCount; j++)
        {
            int sourceColumn = (startColumn + j) % sourceWidth;
            for (int y = 0; y < height; y++)
            {
                target[y * cols + _sweepHead] = source[y * sourceWidth + sourceColumn];
            }

            _sweepHead = (_sweepHead + 1) % cols;
        }

        _lastSeenLiveColumn = _lastLiveColumn;
        return true;
    }

    private double CurrentWindowSeconds()
    {
        if (_viewMode == SpectrogramViewMode.LastBeat)
        {
            return _lastBeatPeriodS > 0.0 ? _lastBeatPeriodS : FallbackBeatPeriodS;
        }

        return _viewSeconds;
    }

    private void UpdateTimeAxis(double windowSeconds)
    {
        // Seconds (one decimal: 0.1, 0.2, …) in Seconds mode; milliseconds in Last
        // Beat mode, whose window is far too short to read in seconds.
        bool lastBeat = _viewMode == SpectrogramViewMode.LastBeat;
        double span = lastBeat ? windowSeconds * 1000.0 : windowSeconds;
        int last = _timeLabels.Length - 1;
        for (int i = 0; i < _timeLabels.Length; i++)
        {
            double value = span * i / last;
            _timeLabels[i].Text = lastBeat ? $"{value:0}" : $"{value:0.#}";
        }

        _timeCaption.Text = lastBeat
            ? "Time (ms) · last beat"
            : $"Time (s) · {windowSeconds:0.#} s window";
    }
}
