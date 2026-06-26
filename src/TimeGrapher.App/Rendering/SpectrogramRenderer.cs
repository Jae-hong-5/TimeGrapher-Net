using Avalonia;
using Avalonia.Controls;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>Spectrogram time-window selector mode (the Qt original's Last Beat / Seconds, plus the multi-beat compare view).</summary>
internal enum SpectrogramViewMode
{
    LastBeat,
    Beats,
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
    /// <summary>Beat period assumed before the detector locks (28800 BPH), for Last Beat.</summary>
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

    // Beats mode: how many consecutive beats are laid out side by side. 2 satisfies
    // "compare one beat with the next"; larger counts make the recurring per-beat
    // energy structure visible as the same bands repeating lane to lane.
    private int _viewBeats = 2;

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

    // Incremental-rebuild trackers: the state the current _sweepBuffer already
    // reflects. When a render only advanced the column count (same window, phase
    // shift, theme background and buffer size), copying just the new columns is
    // identical to re-cropping the whole window — so the trackers let us skip the
    // full O(cols x height) recopy. -1 total forces a full rebuild.
    private long _renderedTotal = -1;
    private long _renderedShift;
    private uint _renderedEmptyColor;
    // Set when the source pixels were recolored (theme toggle) without the column
    // count advancing, so the next render must fully re-crop rather than append.
    private bool _forceFullRebuild;

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
        // A recolor changes the source pixels without advancing the column count,
        // so the next render must fully re-crop — the incremental append would
        // otherwise keep the old colormap. The consumer re-publishes the recolored
        // image via RenderWindowed right after this; rebuild the legend here.
        _forceFullRebuild = true;
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

    /// <summary>
    /// Sets the Beats-mode count (number of consecutive beats shown side by side).
    /// Like the Seconds window this is just a re-crop of the retained history, so
    /// changing it never restarts the sweep. UI thread only.
    /// </summary>
    public void SetViewBeats(int beats)
    {
        _viewBeats = Math.Max(2, beats);
        RenderCurrent();
    }

    public void Reset()
    {
        _lastImage = null;
        _sweepBuffer = null; // drop the previous run's view so the next run starts fresh
        _sweepCols = -1;
        _sweepHead = 0;
        _totalColumns = 0;
        _renderedTotal = -1; // force a full rebuild on the next render
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
        int cols;
        if (_viewMode == SpectrogramViewMode.Beats)
        {
            // Size the window to a whole number of beat-lanes so the lane dividers
            // and the per-lane centering line up to the column (cols / beats is then
            // the exact lane width).
            int beatCols = Math.Max(1, (int)Math.Round(BeatPeriodSeconds() / _lastColumnSeconds));
            cols = beatCols * _viewBeats;
        }
        else
        {
            cols = (int)Math.Round(windowSeconds / _lastColumnSeconds);
        }
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
        // Only Seconds mode is a live time sweep; Last Beat and Beats show
        // phase-locked beats rather than a flow of time, so the live-edge marker
        // is meaningless there and is hidden.
        double width = _spectrogramImage.Bounds.Width;
        if (_viewMode != SpectrogramViewMode.Seconds || _lastImage == null || _sweepCols <= 0 || width <= 0.0)
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
        uint emptyColor = PlotThemePalette.Current.ScopeBg;

        long total = _totalColumns;

        // Right edge of the crop window: both modes sweep the most recent `cols`
        // real columns up to the live edge (total); the phase shift below rotates
        // them so the beat lands where it should, so no column is ever empty.
        long anchor = total;

        // Last Beat phase-locks to the real A onset (re-read each beat) so the beat
        // stays put: shifting the sweep phase by (onset column − cols/2) centers
        // the onset in the window. Beats centers the latest onset in the rightmost
        // lane (half a lane in from the right edge); onsets are one lane (beatCols)
        // apart, so every onset then lands on its own lane center. Seconds mode has
        // no shift (phase = column % cols).
        long shift = 0;
        if (_viewMode == SpectrogramViewMode.LastBeat && _lastBeatOnsetS > 0.0 && _lastColumnSeconds > 0.0)
        {
            shift = (long)Math.Round(_lastBeatOnsetS / _lastColumnSeconds) - cols / 2;
        }
        else if (_viewMode == SpectrogramViewMode.Beats && _lastBeatOnsetS > 0.0 && _lastColumnSeconds > 0.0)
        {
            long onsetColumn = (long)Math.Round(_lastBeatOnsetS / _lastColumnSeconds);
            int beatCols = cols / _viewBeats;
            shift = onsetColumn - cols + beatCols / 2;
        }

        uint[] source = _lastImage!.Pixels;

        bool sizeChanged = _sweepBuffer == null || _sweepBuffer.Width != cols || _sweepBuffer.Height != height;
        // Append only the newly-arrived columns when the window, phase shift, theme
        // background and buffer size are unchanged and the count only advanced;
        // otherwise re-crop the whole window. A retained view column then holds the
        // latest source column for its phase, so appending matches a full crop.
        // Exception: when cols == sourceWidth the window wraps fully and includes
        // the projector's moving live-edge cursor column (DrawLiveEdgeCursor paints
        // the next write column), which mutates a retained column between renders —
        // so re-crop fully there. For cols < sourceWidth the cursor is off-window.
        // Beats re-crops fully every render: its anchor is the latest A onset, which
        // advances independently of the live-edge total, so the append fast-path
        // (keyed on total) does not apply.
        bool canAppend = !sizeChanged
            && !_forceFullRebuild
            && _viewMode != SpectrogramViewMode.Beats
            && cols < sourceWidth
            && _renderedTotal >= 0
            && total >= _renderedTotal
            && shift == _renderedShift
            && emptyColor == _renderedEmptyColor;

        if (sizeChanged)
        {
            _sweepBuffer = new PixelBuffer(cols, height);
        }

        uint[] target = _sweepBuffer!.Pixels;
        if (!canAppend)
        {
            _sweepBuffer.Fill(emptyColor);
            long firstColumn = Math.Max(0, anchor - cols); // only the most recent cols exist
            for (long c = firstColumn; c < anchor; c++)
            {
                WriteSweepColumn(source, target, c, shift, cols, sourceWidth, height);
            }
        }
        else
        {
            // Going back at most cols covers a burst of several columns arriving
            // between renders; nothing is copied when total is unchanged.
            long firstNew = Math.Max(_renderedTotal, total - cols);
            for (long c = firstNew; c < total; c++)
            {
                WriteSweepColumn(source, target, c, shift, cols, sourceWidth, height);
            }
        }

        // Beats: mark each beat boundary with a thin grid-colored divider so the
        // lanes read as separate beats to compare. Painted after the crop so it
        // sits on top; the color comes from the theme (never hardcoded).
        if (_viewMode == SpectrogramViewMode.Beats)
        {
            DrawBeatDividers(target, cols, height);
        }

        _emptyColor = emptyColor;
        _sweepCols = cols;
        _sweepHead = (int)(((anchor - shift) % cols + cols) % cols); // live edge: next column to write
        _renderedTotal = total;
        _renderedShift = shift;
        _renderedEmptyColor = emptyColor;
        _forceFullRebuild = false;
    }

    private static void WriteSweepColumn(uint[] source, uint[] target, long c, long shift, int cols, int sourceWidth, int height)
    {
        int viewColumn = (int)(((c - shift) % cols + cols) % cols);
        int sourceColumn = (int)(c % sourceWidth);
        for (int y = 0; y < height; y++)
        {
            target[y * cols + viewColumn] = source[y * sourceWidth + sourceColumn];
        }
    }

    // Paints a one-column grid-colored line at each lane boundary of the Beats
    // window. The window is sized to whole lanes (cols = beatCols × beats), so the
    // boundaries are exactly between the centered beats — drawing beats−1 dividers
    // splits the window into one lane per beat with no leftover sliver.
    private void DrawBeatDividers(uint[] target, int cols, int height)
    {
        int beatCols = cols / _viewBeats;
        if (beatCols <= 0)
        {
            return;
        }

        uint divider = PlotThemePalette.Current.ScopeGrid;
        for (int m = 1; m < _viewBeats; m++)
        {
            int x = m * beatCols;
            for (int y = 0; y < height; y++)
            {
                target[y * cols + x] = divider;
            }
        }
    }

    /// <summary>The beat period to window by, falling back before the detector locks.</summary>
    private double BeatPeriodSeconds() => _lastBeatPeriodS > 0.0 ? _lastBeatPeriodS : FallbackBeatPeriodS;

    private double CurrentWindowSeconds()
    {
        return _viewMode switch
        {
            SpectrogramViewMode.LastBeat => BeatPeriodSeconds(),
            SpectrogramViewMode.Beats => BeatPeriodSeconds() * _viewBeats,
            _ => _viewSeconds,
        };
    }

    private void UpdateTimeAxis(double windowSeconds)
    {
        // Seconds (one decimal: 0.1, 0.2, …) in Seconds mode; milliseconds in the
        // beat-locked modes (Last Beat / Beats), whose windows are far too short to
        // read in seconds.
        bool beatBased = _viewMode != SpectrogramViewMode.Seconds;
        double span = beatBased ? windowSeconds * 1000.0 : windowSeconds;
        int last = _timeLabels.Length - 1;
        for (int i = 0; i < _timeLabels.Length; i++)
        {
            double value = span * i / last;
            _timeLabels[i].Text = beatBased ? $"{value:0}" : $"{value:0.#}";
        }

        _timeCaption.Text = _viewMode switch
        {
            SpectrogramViewMode.LastBeat => "Time (ms) · last beat",
            SpectrogramViewMode.Beats => $"Time (ms) · last {_viewBeats} beats",
            _ => $"Time (s) · {windowSeconds:0.#} s window",
        };
    }
}
