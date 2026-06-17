using Avalonia;
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
/// Spectrogram tab: the Core projector keeps a full DisplaySeconds (10 s) wrap
/// buffer of STFT columns; this renderer crops the most recent window out of it
/// every render and blits it into the Image control as an oscilloscope sweep
/// (filling left to right, wrapping to overwrite). Because every window is just
/// a crop of the same 10 s source, changing the window keeps all the retained
/// history and never restarts the sweep. The window is the user-selected Last
/// Beat (one beat period) or a fixed number of Seconds; the time axis re-labels
/// to match. The dB color legend is drawn from the same Core LUT the projector
/// colors with, so it can never drift from the image.
/// </summary>
internal sealed class SpectrogramRenderer
{
    /// <summary>Beat period assumed before the detector locks (28800 bph), for Last Beat.</summary>
    private const double FallbackBeatPeriodS = 0.125;

    private readonly Image _spectrogramImage;
    private readonly Image _legendImage;
    // Frequency-axis labels (top..bottom), filled to span 0..Nyquist once a frame
    // gives the column duration (Nyquist = (rows - 1) / (2 * columnSeconds)).
    private readonly TextBlock[] _freqLabels;
    private readonly TextBlock[] _timeLabels;
    private readonly TextBlock _timeCaption;

    // Overlay marking the sweep head — the column where live data is being
    // written right now — positioned over the image on each render.
    private readonly Control _currentLine;

    private bool _light = PlotThemePalette.Current.IsLight;
    private SpectrogramViewMode _viewMode = SpectrogramViewMode.Seconds;
    private double _viewSeconds = 1.0;

    // Latest published image and its windowing metadata, kept so a selector change
    // re-seeds the sweep without waiting for the next frame.
    private PixelBuffer? _lastImage;
    private double _lastColumnSeconds;
    private double _lastBeatPeriodS;

    // Stream time (s) of the most recent detected A (beat) onset. In Last Beat
    // mode the view is phase-locked to it so the beat stays put instead of
    // drifting against a fixed-period grid. 0 before the first beat is captured.
    private double _lastBeatOnsetS;

    // Display buffer for the current window: rebuilt every render by cropping the
    // most recent `cols` columns out of the source's full DisplaySeconds history,
    // so a window change is just a re-crop of the same data — no history is lost
    // and the sweep is not restarted. _sweepHead is the live-edge column
    // (total % cols), where the red marker sits.
    private PixelBuffer? _sweepBuffer;
    private int _sweepCols = -1;
    private int _sweepHead;

    // Monotonic count of source columns written since the run started, supplied by
    // the consumer (which observes every publish, even while the tab is inactive).
    // total % cols gives the sweep head; total bounds how many columns of real
    // (received) data exist.
    private long _totalColumns;

    // The scope-background color the empty (no-input) region is painted with, kept
    // so a theme toggle can recolor just those pixels (the colormap is theme-agnostic).
    private uint _emptyColor = PlotThemePalette.Current.ScopeBg;

    public SpectrogramRenderer(
        Image spectrogramImage,
        Image legendImage,
        TextBlock[] freqLabels,
        TextBlock[] timeLabels,
        TextBlock timeCaption,
        Control currentLine)
    {
        _spectrogramImage = spectrogramImage;
        _legendImage = legendImage;
        _freqLabels = freqLabels;
        _timeLabels = timeLabels;
        _timeCaption = timeCaption;
        _currentLine = currentLine;
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
    /// Follows the theme: the colormap is theme-agnostic, so only the empty
    /// (no-input) background changes — re-crop the view with the new background
    /// and rebuild the legend. UI thread only.
    /// </summary>
    public void ApplyTheme(bool light)
    {
        _light = light;
        _emptyColor = PlotThemePalette.Current.ScopeBg;
        RenderCurrent();
        InitializeLegend();
    }

    /// <summary>Selects the time-window mode and re-crops the view. UI thread only.</summary>
    public void SetViewMode(SpectrogramViewMode mode)
    {
        _viewMode = mode;
        RenderCurrent();
    }

    /// <summary>
    /// Sets the Seconds-mode window length. The view is just re-cropped from the
    /// retained source history, so growing reveals the older columns that were
    /// already captured and shrinking keeps the most recent ones — the sweep is
    /// never restarted. UI thread only.
    /// </summary>
    public void SetViewSeconds(double seconds)
    {
        _viewSeconds = seconds;
        RenderCurrent();
    }

    public void Reset()
    {
        _lastImage = null;
        _sweepBuffer = null; // drop the previous run's view so the next run starts fresh
        _sweepCols = -1;
        _sweepHead = 0;
        _totalColumns = 0;
        _lastBeatOnsetS = 0.0;

        // Always drop the previous run's image — with the tab hidden the bounds are
        // zero and the blank repaint below is skipped, which would leave stale data.
        _spectrogramImage.Source = null;
        _currentLine.IsVisible = false;

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
    /// Stores the latest published image and its windowing metadata, takes the
    /// monotonic source-column count the consumer accumulated, and re-crops the
    /// view. UI thread only.
    /// </summary>
    /// <remarks>
    /// <paramref name="totalColumns"/> is the monotonic count of source columns
    /// written since the run started. The consumer owns it because its
    /// ObserveFrame sees every publish, even while the Spectrogram tab is
    /// inactive — so switching to the tab after the 10 s source buffer has
    /// wrapped still shows the full recent window instead of losing history
    /// (reconstructing it here from the modulo write column would undercount).
    /// </remarks>
    public void RenderWindowed(PixelBuffer image, long totalColumns, double columnSeconds, double beatPeriodS, double beatOnsetS)
    {
        _totalColumns = totalColumns;
        _lastImage = image;
        _lastColumnSeconds = columnSeconds;
        _lastBeatPeriodS = beatPeriodS;
        _lastBeatOnsetS = beatOnsetS;
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        double windowSeconds = CurrentWindowSeconds();
        if (_lastImage == null || _lastColumnSeconds <= 0.0)
        {
            UpdateTimeAxis(windowSeconds);
            UpdateCurrentLine();
            return;
        }

        int sourceWidth = _lastImage.Width;
        int height = _lastImage.Height;
        int cols = (int)Math.Round(windowSeconds / _lastColumnSeconds);
        cols = Math.Clamp(cols, 1, sourceWidth);

        RebuildView(cols, sourceWidth, height);

        PixelBufferBitmap.UpdateImage(_spectrogramImage, _sweepBuffer!);
        UpdateTimeAxis(cols * _lastColumnSeconds); // label the window actually shown
        UpdateFrequencyAxis(height);
        UpdateCurrentLine();
    }

    // Labels the frequency axis 0..Nyquist (sampleRate / 2). The image's top row
    // is the Nyquist bin, so with rows = fftSize/2 + 1 and columnSeconds = hop /
    // sampleRate, Nyquist = (rows - 1) / (2 * columnSeconds). Labels run top
    // (Nyquist) to bottom (0), evenly spaced like their fixed slot positions.
    private void UpdateFrequencyAxis(int rows)
    {
        if (rows < 2 || _lastColumnSeconds <= 0.0)
        {
            return;
        }

        double nyquistHz = (rows - 1) / (2.0 * _lastColumnSeconds);
        int last = _freqLabels.Length - 1;
        for (int i = 0; i < _freqLabels.Length; i++)
        {
            double hz = nyquistHz * (last - i) / last; // i = 0 is the top (Nyquist)
            _freqLabels[i].Text = hz >= 1000.0 ? $"{hz / 1000.0:0} kHz" : $"{hz:0} Hz";
        }
    }

    // Positions the live-head marker over the image at the sweep head column
    // (head / cols of the displayed width). Hidden until there is data and the
    // image has been laid out.
    private void UpdateCurrentLine()
    {
        // Last Beat shows one beat repeatedly, not a flow of time, so the live-edge
        // marker is meaningless there and is hidden.
        double width = _spectrogramImage.Bounds.Width;
        if (_viewMode == SpectrogramViewMode.LastBeat || _lastImage == null || _sweepCols <= 0 || width <= 0.0)
        {
            _currentLine.IsVisible = false;
            return;
        }

        double x = (double)_sweepHead / _sweepCols * width;
        _currentLine.Margin = new Thickness(x, 0.0, 0.0, 0.0);
        _currentLine.IsVisible = true;
    }

    // Rebuild the displayed window by cropping the most recent `cols` columns out
    // of the source's full DisplaySeconds history into a sweep of width `cols`.
    // The not-yet-received region (early in a run) is painted the scope BACKGROUND
    // color so it reads as empty, distinct from the dB-floor canvas color that
    // means "received, but quiet". Each source column keeps its sweep phase
    // (absolute index % cols) so the live edge lands at total % cols — which makes
    // a window change a pure re-crop: growing reveals older retained columns,
    // shrinking keeps the most recent, neither restarts the sweep.
    private void RebuildView(int cols, int sourceWidth, int height)
    {
        if (_sweepBuffer == null || _sweepBuffer.Width != cols || _sweepBuffer.Height != height)
        {
            _sweepBuffer = new PixelBuffer(cols, height);
        }

        _emptyColor = PlotThemePalette.Current.ScopeBg;
        _sweepBuffer.Fill(_emptyColor);

        // Last Beat phase-locks to the real A onset (re-read each beat) so the beat
        // stays put: shifting the sweep phase by (onset column − cols/2) centers
        // the onset in the window. Seconds mode has no shift (phase = column % cols).
        long shift = 0;
        if (_viewMode == SpectrogramViewMode.LastBeat && _lastBeatOnsetS > 0.0 && _lastColumnSeconds > 0.0)
        {
            shift = (long)Math.Round(_lastBeatOnsetS / _lastColumnSeconds) - cols / 2;
        }

        long total = _totalColumns;
        long firstColumn = Math.Max(0, total - cols); // only the most recent cols exist
        uint[] source = _lastImage!.Pixels;
        uint[] target = _sweepBuffer.Pixels;
        for (long c = firstColumn; c < total; c++)
        {
            int viewColumn = (int)(((c - shift) % cols + cols) % cols);
            int sourceColumn = (int)(c % sourceWidth);
            for (int y = 0; y < height; y++)
            {
                target[y * cols + viewColumn] = source[y * sourceWidth + sourceColumn];
            }
        }

        _sweepCols = cols;
        _sweepHead = (int)(((total - shift) % cols + cols) % cols); // live edge: next column to write
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
