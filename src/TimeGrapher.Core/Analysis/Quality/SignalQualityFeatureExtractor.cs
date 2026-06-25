using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis.Quality;

/// <summary>
/// Builds a <see cref="SignalQualityFeatures"/> window from the running detector
/// state plus the A events seen in each block. Pure Core math with a small
/// rolling state and no allocation in steady state; the same extractor feeds
/// both the live classifier and the offline trainer so their inputs cannot
/// drift apart.
///
/// Feed it the detector's instantaneous levels and the block's events via
/// <see cref="Observe"/>; read the current window with <see cref="TryGetFeatures"/>.
/// </summary>
public sealed class SignalQualityFeatureExtractor
{
    private const double Epsilon = 1e-9;

    private readonly int _window;

    // Per-A-event ring buffers (most recent <= _window entries).
    private readonly double[] _intervals;
    private readonly double[] _peaks;
    private readonly ulong[] _missedAt;
    private readonly uint[] _syncLossAt;
    private int _count;
    private int _head;
    private double _lastASample;
    private bool _hasLastA;

    // Per-observation (per-block) ring of synced flags.
    private readonly bool[] _syncedObs;
    private int _obsCount;
    private int _obsHead;

    // Latest instantaneous detector levels.
    private bool _hasSnapshot;
    private double _referencePeak;
    private double _noiseFloor;
    private double _minPeakThreshold;

    /// <param name="window">
    /// Number of recent A events (and observations) the window spans. Clamped to
    /// a minimum of 4. The default 32 is roughly four seconds at 28800 BPH.
    /// </param>
    public SignalQualityFeatureExtractor(int window = 32)
    {
        if (window < 4)
        {
            window = 4;
        }

        _window = window;
        _intervals = new double[window];
        _peaks = new double[window];
        _missedAt = new ulong[window];
        _syncLossAt = new uint[window];
        _syncedObs = new bool[window];
    }

    /// <summary>Clear all rolling state (e.g. on a detector reset / new run).</summary>
    public void Reset()
    {
        _count = 0;
        _head = 0;
        _hasLastA = false;
        _obsCount = 0;
        _obsHead = 0;
        _hasSnapshot = false;
    }

    /// <summary>
    /// Record one analysis block: the detector's instantaneous levels, the
    /// session-cumulative fault counters, and the events emitted in the block.
    /// Only A events advance the timing/peak rings.
    /// </summary>
    public void Observe(
        bool synced,
        float referencePeak,
        float noiseFloor,
        float minPeakThreshold,
        ulong missedBeats,
        uint syncLossCount,
        ReadOnlySpan<TgEvent> events)
    {
        _hasSnapshot = true;
        _referencePeak = referencePeak;
        _noiseFloor = noiseFloor;
        _minPeakThreshold = minPeakThreshold;

        _syncedObs[_obsHead] = synced;
        _obsHead = (_obsHead + 1) % _window;
        if (_obsCount < _window)
        {
            _obsCount++;
        }

        foreach (TgEvent ev in events)
        {
            if (ev.Type != TgEventType.A)
            {
                continue;
            }

            double sample = ev.SampleIndex + ev.SubSampleOffset;
            if (_hasLastA)
            {
                double interval = sample - _lastASample;
                if (interval > 0.0)
                {
                    _intervals[_head] = interval;
                    _peaks[_head] = ev.PeakValue;
                    _missedAt[_head] = missedBeats;
                    _syncLossAt[_head] = syncLossCount;
                    _head = (_head + 1) % _window;
                    if (_count < _window)
                    {
                        _count++;
                    }
                }
            }

            _lastASample = sample;
            _hasLastA = true;
        }
    }

    /// <summary>
    /// Produce the current feature window. Returns false until enough A events
    /// have accumulated (at least 4) and at least one block has been observed.
    /// </summary>
    public bool TryGetFeatures(out SignalQualityFeatures features)
    {
        features = default;
        if (!_hasSnapshot || _count < 4)
        {
            return false;
        }

        double sumI = 0.0, sumI2 = 0.0, sumP = 0.0, sumP2 = 0.0;
        ulong missedMin = ulong.MaxValue, missedMax = 0;
        uint syncLossMin = uint.MaxValue, syncLossMax = 0;

        for (int k = 0; k < _count; k++)
        {
            double iv = _intervals[k];
            sumI += iv;
            sumI2 += iv * iv;

            double pk = _peaks[k];
            sumP += pk;
            sumP2 += pk * pk;

            // Cumulative counters are monotonic, so window min/max == oldest/newest
            // regardless of ring order.
            ulong m = _missedAt[k];
            if (m < missedMin) { missedMin = m; }
            if (m > missedMax) { missedMax = m; }

            uint s = _syncLossAt[k];
            if (s < syncLossMin) { syncLossMin = s; }
            if (s > syncLossMax) { syncLossMax = s; }
        }

        double intervalCv = CoefficientOfVariation(sumI, sumI2, _count);
        double peakCv = CoefficientOfVariation(sumP, sumP2, _count);

        double snrDb = 20.0 * Math.Log10(Math.Max(_referencePeak, Epsilon) / Math.Max(_noiseFloor, Epsilon));
        double peakMargin = Math.Max(_referencePeak, 0.0) / Math.Max(_minPeakThreshold, Epsilon);

        double missedRate = (double)(missedMax - missedMin) / _count;
        double syncLossRate = (double)(syncLossMax - syncLossMin) / _count;

        int syncedTrue = 0;
        for (int k = 0; k < _obsCount; k++)
        {
            if (_syncedObs[k])
            {
                syncedTrue++;
            }
        }

        double syncedFraction = _obsCount > 0 ? (double)syncedTrue / _obsCount : 0.0;

        features = new SignalQualityFeatures(
            (float)snrDb,
            (float)peakMargin,
            (float)_noiseFloor,
            (float)intervalCv,
            (float)peakCv,
            (float)missedRate,
            (float)syncLossRate,
            (float)syncedFraction);
        return true;
    }

    private static double CoefficientOfVariation(double sum, double sumSquares, int count)
    {
        double mean = sum / count;
        if (Math.Abs(mean) < Epsilon)
        {
            return 0.0;
        }

        double variance = (sumSquares / count) - (mean * mean);
        if (variance < 0.0)
        {
            variance = 0.0;
        }

        return Math.Sqrt(variance) / Math.Abs(mean);
    }
}
