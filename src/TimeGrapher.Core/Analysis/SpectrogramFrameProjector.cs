using System.Diagnostics;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Time-frequency spectrogram projector: a short-time Fourier transform over the
/// raw input blocks (the same span AnalysisWorker hands the detector pipeline).
/// Each completed hop renders one image column — x = time (wrap-writing recent
/// window), y = frequency (low at the bottom), color = intensity in dB through
/// the fixed inferno-like LUT.
///
/// FFT size is the power of two nearest ~21 ms of input (1024 @ 48 kHz,
/// ~47 Hz/bin), hop = size/2. Display rows cover bins 0..~12 kHz only: watch
/// tick/tock acoustics concentrate in the low kHz, so the upper half of the
/// Nyquist range would waste vertical resolution on empty band.
///
/// Publishing copies the working image into a fixed three-buffer pool (the
/// SoundPrintFrameProjector pattern — SAP performance tactic: maintain multiple
/// copies of data): a published buffer is overwritten again only after two newer
/// publishes, and the render scheduler's latest-wins delivery keeps the UI
/// within one publish of the newest image, so on-screen reads never touch a
/// buffer being recycled. Zero steady-state allocations: the FFT scratch, FIFO
/// and pool buffers are all preallocated. ProcessSamples/AppendSnapshot run on
/// the analysis thread only.
/// </summary>
public sealed class SpectrogramFrameProjector
{
    public const int PublishIntervalMs = 100;
    private const int PublishBufferCount = 3;

    /// <summary>Analysis window length (s) rounded to the nearest power-of-two sample count.</summary>
    private const double WindowSeconds = 0.021;

    /// <summary>Top of the display band; watch acoustics concentrate below this.</summary>
    public const double MaxDisplayFrequencyHz = 12000.0;

    /// <summary>dB floor of the color scale (quiet end). 0 dB = full-scale sinusoid.</summary>
    public const double DbFloor = -70.0;

    /// <summary>dB ceiling of the color scale (loud end); the range matches the original timegrapher.</summary>
    public const double DbCeiling = -10.0;

    /// <summary>Recent time window the image holds (seconds of columns).</summary>
    public const int DisplaySeconds = 10;

    /// <summary>Live-edge cursor color (not part of the LUT, so it never reads as energy).</summary>
    public const uint LiveEdgeColor = 0xFF7F7F7Fu;

    private static readonly uint[] Lut = BuildViridisLut();
    // One colormap for both themes: the theme only changes the empty (no-input)
    // background, which the renderer fills with the scope background color.
    private static readonly uint[] LutLight = Lut;
    private static readonly IReadOnlyDictionary<uint, uint> DarkToLight = BuildIndexMap(Lut, LutLight);
    private static readonly IReadOnlyDictionary<uint, uint> LightToDark = BuildIndexMap(LutLight, Lut);

    /// <summary>64-entry viridis intensity LUT (index 0 = dB floor, 63 = dB ceiling). Shared with the UI legend.</summary>
    public static IReadOnlyList<uint> ColorLut => Lut;

    /// <summary>Same viridis colormap as <see cref="ColorLut"/> (kept for the themed call site).</summary>
    public static IReadOnlyList<uint> ColorLutLight => LutLight;

    /// <summary>The colormap to color with / draw the legend from for the requested theme.</summary>
    public static IReadOnlyList<uint> ColorLutFor(bool light) => light ? LutLight : Lut;

    private readonly int _fftSize;
    private readonly int _hop;
    private readonly PixelBuffer _image;
    private readonly PixelBuffer[] _publishBuffers = new PixelBuffer[PublishBufferCount];
    private readonly float[] _window;
    private readonly float[] _fifo;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly double _fullScaleMagnitude;
    private readonly double _columnSeconds;
    private readonly Stopwatch _publishTimer = new();

    private uint[] _activeLut;
    private bool _light;
    private int _fifoFill;
    private int _writeColumn;
    private int _nextPublishBuffer;
    private int _publishIntervalScale = 1;
    private bool _livePreviewEnabled = true;
    private bool _recolorPending;

    public SpectrogramFrameProjector(int sampleRate, bool light = false)
    {
        _light = light;
        _activeLut = light ? LutLight : Lut;
        _fftSize = NearestPowerOfTwo(sampleRate * WindowSeconds);
        _hop = _fftSize / 2;
        _columnSeconds = (double)_hop / sampleRate;

        int rows = Math.Min(_fftSize / 2, (int)(MaxDisplayFrequencyHz * _fftSize / sampleRate)) + 1;
        int width = (int)Math.Ceiling(DisplaySeconds * (double)sampleRate / _hop);
        _image = new PixelBuffer(width, rows);
        _image.Fill(_activeLut[0]);
        for (int i = 0; i < PublishBufferCount; ++i)
        {
            _publishBuffers[i] = new PixelBuffer(width, rows);
        }

        _window = new float[_fftSize];
        Fft.FillHannWindow(_window);
        _fifo = new float[_fftSize];
        _re = new double[_fftSize];
        _im = new double[_fftSize];

        // Hann coherent gain is 0.5 and a real sinusoid splits across +/- bins,
        // so a full-scale sine peaks at N/4 — that is the 0 dB reference.
        _fullScaleMagnitude = _fftSize / 4.0;
    }

    public int FftSize => _fftSize;
    public int HopSize => _hop;
    public int Width => _image.Width;
    public int Height => _image.Height;
    public int CurrentColumn => _writeColumn;

    /// <summary>
    /// Deadline-degradation knob: disable/re-enable the live-edge cursor column
    /// redrawn after every block (the sound-print live-column knob analogue).
    /// Analysis thread only.
    /// </summary>
    public void SetLivePreviewEnabled(bool enabled)
    {
        _livePreviewEnabled = enabled;
    }

    /// <summary>
    /// Deadline-degradation knob: stretch the publish interval by an integer
    /// factor (1 = the default 100 ms cadence). Analysis thread only.
    /// </summary>
    public void SetPublishIntervalScale(int scale)
    {
        _publishIntervalScale = Math.Max(1, scale);
    }

    /// <summary>
    /// Switches the spectrogram colormap to match the UI theme (light = reversed
    /// inferno) and flags the image for republish. Existing columns are remapped
    /// in place so the whole window recolors at once, not just new columns.
    /// Analysis thread only (the SetSoundBackgroundColor recolor flow).
    /// </summary>
    public void SetColormap(bool light)
    {
        if (light == _light)
        {
            return;
        }

        _light = light;
        _activeLut = light ? LutLight : Lut;
        MirrorColormap(_image.Pixels, toLight: light);
        _recolorPending = true;
    }

    /// <summary>
    /// Feeds one raw audio block. Whole hops render columns immediately; the
    /// remainder stays in the FIFO for the next block.
    /// </summary>
    public void ProcessSamples(ReadOnlySpan<float> block)
    {
        int offset = 0;
        while (offset < block.Length)
        {
            int take = Math.Min(block.Length - offset, _fftSize - _fifoFill);
            block.Slice(offset, take).CopyTo(_fifo.AsSpan(_fifoFill));
            _fifoFill += take;
            offset += take;

            if (_fifoFill == _fftSize)
            {
                RenderColumn();
                Array.Copy(_fifo, _hop, _fifo, 0, _fftSize - _hop);
                _fifoFill = _fftSize - _hop;
            }
        }

        if (_livePreviewEnabled)
        {
            DrawLiveEdgeCursor();
        }
    }

    public void AppendSnapshot(AnalysisFrame frame, bool force = false)
    {
        if (force ||
            _recolorPending ||
            !_publishTimer.IsRunning ||
            _publishTimer.ElapsedMilliseconds >= (long)PublishIntervalMs * _publishIntervalScale)
        {
            PixelBuffer snapshot = _publishBuffers[_nextPublishBuffer];
            _nextPublishBuffer = (_nextPublishBuffer + 1) % PublishBufferCount;
            Array.Copy(_image.Pixels, snapshot.Pixels, snapshot.Pixels.Length);
            frame.SpectrogramImage = snapshot;
            frame.SpectrogramImageUpdated = true;
            frame.SpectrogramLiveColumn = _writeColumn;
            frame.SpectrogramColumnSeconds = _columnSeconds;
            _recolorPending = false;
            _publishTimer.Restart();
        }
    }

    private void RenderColumn()
    {
        for (int i = 0; i < _fftSize; i++)
        {
            _re[i] = _fifo[i] * _window[i];
            _im[i] = 0.0;
        }

        Fft.Forward(_re, _im);

        int width = _image.Width;
        int rows = _image.Height;
        uint[] pixels = _image.Pixels;
        for (int bin = 0; bin < rows; bin++)
        {
            double re = _re[bin];
            double im = _im[bin];
            double magnitude = Math.Sqrt(re * re + im * im) / _fullScaleMagnitude;
            double db = 20.0 * Math.Log10(Math.Max(magnitude, 1e-12));
            double t = (db - DbFloor) / (DbCeiling - DbFloor);
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }

            int y = rows - 1 - bin; // low frequency at the bottom
            pixels[y * width + _writeColumn] = _activeLut[(int)(t * (_activeLut.Length - 1) + 0.5)];
        }

        _writeColumn = (_writeColumn + 1) % width;
    }

    /// <summary>
    /// Marks the wrap-writing live edge: the next column to be written is painted
    /// a neutral gray, overwritten again by the next completed hop.
    /// </summary>
    private void DrawLiveEdgeCursor()
    {
        int width = _image.Width;
        uint[] pixels = _image.Pixels;
        for (int y = 0; y < _image.Height; y++)
        {
            pixels[y * width + _writeColumn] = LiveEdgeColor;
        }
    }

    /// <summary>
    /// Recolors a spectrogram image between the dark and light colormaps in place,
    /// mapping each pixel by its intensity index (<paramref name="toLight"/> picks
    /// the direction); the live-edge cursor and any non-LUT pixel pass through
    /// unchanged. Used by the projector's live recolor and, after a stop, to
    /// recolor the kept frozen image on a theme toggle (no live worker).
    /// </summary>
    public static void MirrorColormap(uint[] pixels, bool toLight)
    {
        IReadOnlyDictionary<uint, uint> map = toLight ? DarkToLight : LightToDark;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (map.TryGetValue(pixels[i], out uint mapped))
            {
                pixels[i] = mapped;
            }
        }
    }

    private static IReadOnlyDictionary<uint, uint> BuildIndexMap(uint[] from, uint[] to)
    {
        var map = new Dictionary<uint, uint>(from.Length);
        for (int i = 0; i < from.Length; i++)
        {
            map[from[i]] = to[i];
        }

        return map;
    }

    private static int NearestPowerOfTwo(double target)
    {
        int pow = 1;
        while (pow < target)
        {
            pow <<= 1;
        }

        return pow > 1 && target - (pow >> 1) < pow - target ? pow >> 1 : pow;
    }

    /// <summary>
    /// Hand-coded viridis LUT: 64 entries linearly interpolated between the
    /// canonical viridis anchor colors (dark purple → blue → green → yellow),
    /// opaque — matching the original timegrapher's spectrogram colors.
    /// </summary>
    private static uint[] BuildViridisLut()
    {
        // (R, G, B) viridis anchors at t = 0.0, 0.111, ..., 1.0.
        byte[,] anchors =
        {
            { 68, 1, 84 },
            { 72, 40, 120 },
            { 62, 74, 137 },
            { 49, 104, 142 },
            { 38, 130, 142 },
            { 31, 158, 137 },
            { 53, 183, 121 },
            { 109, 205, 89 },
            { 180, 222, 44 },
            { 253, 231, 37 },
        };
        return InterpolateAnchors(anchors);
    }

    /// <summary>64-entry opaque LUT linearly interpolated across the (R,G,B) anchor rows.</summary>
    private static uint[] InterpolateAnchors(byte[,] anchors)
    {
        int anchorCount = anchors.GetLength(0);
        var lut = new uint[64];
        for (int i = 0; i < lut.Length; i++)
        {
            double position = i / (double)(lut.Length - 1) * (anchorCount - 1);
            int low = (int)position;
            int high = Math.Min(low + 1, anchorCount - 1);
            double frac = position - low;

            byte Channel(int channel) => (byte)Math.Round(
                anchors[low, channel] + (anchors[high, channel] - anchors[low, channel]) * frac);

            lut[i] = Argb.Rgba(Channel(0), Channel(1), Channel(2));
        }

        return lut;
    }
}
