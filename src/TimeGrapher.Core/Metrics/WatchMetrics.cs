using System;
using System.Collections.Generic;
using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Metrics;

/// <summary>Port of WatchMetricsConfig (WatchMetrics.h).</summary>
public struct WatchMetricsConfig
{
    public int SampleRate;          // 48000
    public double LiftAngle;        // 52.0
    public int AveragingPeriod;     // 2
    public int MaxRateDataPoints;   // 250
    public double RateErrorYScale;  // 10.0

    // Seconds added to the measured A->C interval before the amplitude formula, to
    // offset the detector's onset-detection latency (the A onset is timestamped at a
    // threshold crossing, which lags the true onset more than the C peak does, so the
    // raw A->C is slightly short and amplitude reads high). 0 = no compensation, which
    // keeps the bare formula behaviour for direct callers/unit tests; the live pipeline
    // sets it from DetectorMetricsEngineConfig.AmplitudeOnsetLatencyS.
    public double AmplitudeOnsetLatencyS;

    /// <summary>Mirror of the C++ struct's in-class default member initializers.</summary>
    public WatchMetricsConfig()
    {
        SampleRate = 48000;
        LiftAngle = 52.0;
        AveragingPeriod = 2;
        MaxRateDataPoints = 250;
        RateErrorYScale = 10.0;
        AmplitudeOnsetLatencyS = 0.0;
    }
}

/// <summary>
/// Port of WatchMetrics (WatchMetrics.h/.cpp): computes Error Rate, beat error and
/// amplitude from detected A/C tick events and formats the result/marker text.
/// </summary>
public sealed class WatchMetrics
{
    // Local constants from the anonymous namespace in WatchMetrics.cpp.
    private const int Tic = 0;
    private const int Toc = 1;
    private const int GraphRateWindowInitialCapacity = 100;
    private const int GraphRateWarmupPoints = 6;

    private readonly WatchMetricsConfig _config;

    private ulong _ticTocBeatNumber = 0;
    private readonly List<double> _xTic = new();
    private readonly List<double> _xToc = new();
    private readonly List<double> _yTic = new();
    private readonly List<double> _yToc = new();
    private int _xTicIndex = 0;
    private int _xTocIndex = 0;
    private bool _haveStartTime = false;
    private bool _haveZeroOffset = false;
    private double _startTime = 0.0;
    private double _zeroOffsetValue = 0.0;
    private readonly RollingLeastSquares _graphRlsTicRate;
    private readonly RollingLeastSquares _graphRlsTocRate;
    private double _graphRateSPerDay = 0.0;
    private bool _graphRateValid = false;
    private double _displayRateSPerDay = 0.0;
    private double _rateWindowStartS = 0.0;
    private double _rateWindowStartGraphX = 0.0;
    private double _rateWindowDeltaSumS = 0.0;
    private int _rateWindowDeltaCount = 0;
    private readonly double[] _lastRatePhaseEventS = { 0.0, 0.0 };
    private readonly bool[] _haveRatePhaseEvent = { false, false };
    private bool _haveAveragePeriodRateInterval = false;
    private AveragePeriodRateInterval _lastAveragePeriodRateInterval;
    private int _bph = 0;
    private bool _bphValid = false;
    private int _beatsPerSecondWindow = 0;

    private readonly double[] _beatErrorTimes = { 0.0, 0.0, 0.0 };
    private int _beatErrorIdx = 0;
    private double _beatErrorMs = 0.0;
    private double _displayBeatErrorMs = 0.0;
    private bool _displayBeatErrorValid = false;
    private double _displayBeatErrorWindowStartS = 0.0;
    private double _displayBeatErrorWindowSumMs = 0.0;
    private int _displayBeatErrorWindowCount = 0;

    private double _lastAEvent = 0.0;
    private bool _haveAEvent = false;
    private double _displayAmplitudeDeg = 0.0;
    private bool _displayAmplitudeValid = false;
    private double _displayAmplitudeWindowStartS = 0.0;
    private double _displayAmplitudeWindowSumDeg = 0.0;
    private int _displayAmplitudeWindowCount = 0;

    // Title-bar Amplitude / Beat Error: per-beat rolling means over the same paired-amplitude
    // and clean-window beat-error samples the avg-period display sums, but as a sliding window
    // updated every beat, so all three title-bar readouts refresh per beat (matching the rolling
    // graph-rate Error Rate). Title-bar only: these intentionally diverge from the avg-period
    // values the Long-Term / Waveform-Compare views and the measurement CSV still show. The
    // window is sized to one averaging period on lock.
    private readonly RollingAverage _titleAmplitudeDeg = new(0);
    private readonly RollingAverage _titleBeatErrorMs = new(0);
    private double _amplitudeTic = 0.0;
    private double _amplitudeToc = 0.0;
    private bool _amplitudeTicValid = false;
    private const int AcHistoryCount = 8;
    private const double AcDeviationFloorMs = 1.5;
    private const double AcDeviationMadScale = 4.0;
    private readonly double[] _recentAcMs = new double[AcHistoryCount];
    private int _recentAcHead;
    private int _recentAcCount;

    // Derived timing measures (project plan "Expected Enhancements"): DiffTicTac,
    // DiffPeriod (short fixed window) and AvgPeriod (since start / last segment
    // restart on a re-lock at a different BPH).
    private const int DiffPeriodWindowSeconds = 4;
    private readonly RollingAverage _rollPeriodDelta = new(0);
    private double _avgPeriodDeltaSumMs = 0.0;
    private long _avgPeriodDeltaCount = 0;
    private bool _skipNextPeriodDelta = false;
    private double _diffTicTacMs = 0.0;
    private bool _diffTicTacValid = false;
    private double _signedBeatErrorMs = 0.0;
    private bool _signedBeatErrorValid = false;
    private ulong _missedBeats = 0;

    // Per-event numeric stash consumed by the BeatTimingSample/AmplitudeSample
    // emission in HandleAEvent/HandleCEvent. _lastRateErrorMs is always fresh when
    // the emission gate (_haveStartTime && _bphValid) holds, because that is the
    // same condition under which ComputeRateError's synced branch just ran.
    private double _lastRateErrorMs = 0.0;
    private bool _lastAmplitudeInstValid = false;
    private double _lastAmplitudeInstDeg = 0.0;
    private bool _lastAmplitudePairUpdated = false;
    private double _lastAmplitudePairDeg = 0.0;

    public WatchMetrics(WatchMetricsConfig config)
    {
        _config = config;
        _graphRlsTicRate = new RollingLeastSquares(GraphRateWindowInitialCapacity);
        _graphRlsTocRate = new RollingLeastSquares(GraphRateWindowInitialCapacity);
        // mX*/mY*.reserve(max_rate_data_points) in the original is a capacity hint only.
    }

    public void Reset()
    {
        _haveAEvent = false;
        _amplitudeTicValid = false;

        _beatErrorIdx = 0;

        _bphValid = false;
        _xTicIndex = 0;
        _xTocIndex = 0;
        _startTime = 0.0;
        _haveStartTime = false;
        _haveZeroOffset = false;
        _zeroOffsetValue = 0.0;
        _xTic.Clear();
        _xToc.Clear();
        _yTic.Clear();
        _yToc.Clear();
        ResetGraphRate();
        ResetRateAveraging(0.0);
        ResetDisplayAveraging(0.0);

        ResetDerivedMeasures();
        _missedBeats = 0;
        _lastAmplitudeInstValid = false;
        _lastAmplitudePairUpdated = false;
        _recentAcHead = 0;
        _recentAcCount = 0;
    }

    private void ResetDerivedMeasures()
    {
        _rollPeriodDelta.Reset();
        _avgPeriodDeltaSumMs = 0.0;
        _avgPeriodDeltaCount = 0;
        _skipNextPeriodDelta = true;
        _diffTicTacValid = false;
        _signedBeatErrorValid = false;
    }

    private void ResetRateAveraging(double startTimeS)
    {
        _displayRateSPerDay = 0.0;
        _rateWindowStartS = startTimeS;
        _rateWindowStartGraphX = CurrentRateGraphX();
        ResetRateWindow();
        _haveRatePhaseEvent[Tic] = false;
        _haveRatePhaseEvent[Toc] = false;
        _haveAveragePeriodRateInterval = false;
        _lastAveragePeriodRateInterval = default;
    }

    private void ResetGraphRate()
    {
        _graphRateSPerDay = 0.0;
        _graphRateValid = false;
        _graphRlsTicRate.Reset();
        _graphRlsTocRate.Reset();
    }

    private void ResetRateWindow()
    {
        _rateWindowDeltaSumS = 0.0;
        _rateWindowDeltaCount = 0;
    }

    private void ResetDisplayAveraging(double startTimeS)
    {
        _displayBeatErrorMs = 0.0;
        _displayBeatErrorValid = false;
        _displayBeatErrorWindowStartS = startTimeS;
        ResetDisplayBeatErrorWindow();

        _displayAmplitudeDeg = 0.0;
        _displayAmplitudeValid = false;
        _displayAmplitudeWindowStartS = startTimeS;
        ResetDisplayAmplitudeWindow();

        // The title-bar rolling means restart with the display averaging (full reset,
        // re-sync, or detection gap) so a stale pre-segment sample cannot leak across.
        _titleAmplitudeDeg.Reset();
        _titleBeatErrorMs.Reset();
    }

    private void ResetDisplayBeatErrorWindow()
    {
        _displayBeatErrorWindowSumMs = 0.0;
        _displayBeatErrorWindowCount = 0;
    }

    private void ResetDisplayAmplitudeWindow()
    {
        _displayAmplitudeWindowSumDeg = 0.0;
        _displayAmplitudeWindowCount = 0;
    }

    public WatchMetricsUpdate HandleAEvent(double eventSample, bool haveValidBph, double bph)
    {
        var update = new WatchMetricsUpdate();
        if (!ComputeRateError(eventSample, haveValidBph, bph, update))
        {
            return update;
        }
        ComputeBeatError(eventSample, update);

        if (_haveStartTime && _bphValid)
        {
            update.SetBeatTimingSample(new BeatTimingSample(
                _ticTocBeatNumber,
                eventSample / (double)_config.SampleRate,
                CurrentBeatPhase() == Tic,
                _lastRateErrorMs,
                _graphRateValid,
                _graphRateSPerDay,
                _signedBeatErrorValid,
                _signedBeatErrorMs,
                _bph));

            update.SetDerivedMeasures(new DerivedTimingMeasures(
                _diffTicTacValid,
                _diffTicTacMs,
                _rollPeriodDelta.CurrentSize() > 0,
                _rollPeriodDelta.GetAverage(),
                _avgPeriodDeltaCount > 0,
                _avgPeriodDeltaCount > 0 ? _avgPeriodDeltaSumMs / _avgPeriodDeltaCount : 0.0));
        }

        _haveAEvent = true;
        _lastAEvent = eventSample;
        return update;
    }

    public WatchMetricsUpdate HandleCEvent(double eventSample, bool haveValidBph, double bph)
    {
        var update = new WatchMetricsUpdate();
        double beatTimeSeconds = _haveAEvent
            ? (eventSample - _lastAEvent) / (double)_config.SampleRate
            : 0.0;

        update.SetCMarkerText(FormatCMarkerText(beatTimeSeconds, haveValidBph, bph));

        ComputeAmplitude(eventSample, bph, update);
        update.SetResults(FormatResults());

        if (_lastAmplitudeInstValid || _lastAmplitudePairUpdated)
        {
            update.SetAmplitudeSample(new AmplitudeSample(
                eventSample / (double)_config.SampleRate,
                _lastAmplitudeInstValid,
                _lastAmplitudeInstDeg,
                _lastAmplitudePairUpdated,
                _lastAmplitudePairDeg));
        }

        return update;
    }

    public int CurrentBeatPhase()
    {
        return (int)((_ticTocBeatNumber - 1) & 1);
    }

    /// <summary>
    /// Session-cumulative count of beats the detector skipped over (A-to-A intervals
    /// spanning more than one nominal beat). QA evidence for the low-missed-beats
    /// requirement; reset only with <see cref="Reset"/>, not on sync re-acquisition.
    /// </summary>
    public ulong MissedBeats => _missedBeats;

    // Balance amplitude from the A->C interval, using the SYMMETRIC HALF-LIFT model:
    //
    //     Amp = liftAngle / (2 * sin(pi * t_AC / (2T))),   T = 3600 / BPH
    //
    // This formula is a DELIBERATE, physics-driven choice. For rigorous accuracy we
    // intentionally use NEITHER of the two other forms that exist in this project:
    //
    //   * NOT the original Qt code's formula  liftAngle / sin(pi * t_AC / T)  (the
    //     "full-lift" form this method shipped with). It implicitly assumes the
    //     balance sweeps the whole lift angle from the dead-point; it is internally
    //     consistent but is NOT the inverse of the physical swing, so it over-reads
    //     (a true 270 deg reads ~271.3 deg, the error growing at low amplitude).
    //
    //   * NOT the requirement doc's formula either (TimeGrapher Equations_v1.md,
    //     Part IV:  Amp = 3600 * lambda / (pi * BPH * t_AC)). That is only the
    //     SMALL-ANGLE LINEAR approximation -- it drops the sine curvature.
    //
    // We use the half-lift sine form because it is the rigorous model. The balance
    // swings theta(t) = A * sin(pi * t / T); the impulse/lift zone is centered on the
    // dead-point, so the A and C landmarks sit at -lambda/2 and +lambda/2 and t_AC
    // spans the full lift zone. Inverting that geometry gives sin(pi * t_AC / (2T)) =
    // lambda / (2A), i.e. the expression below. Consequences:
    //   - it is the EXACT inverse of the synthesiser's A->C model
    //     (WatchSynthStream.ComputeAToCTimeS), so a configured amplitude is recovered
    //     without formula bias; and
    //   - it matches the canonical open-source timegrapher "tg" (vacaboja), whose
    //     amplitude is 0.5 * liftAngle / sin(pi * pulse / period_full).
    // Note 7200/BPH == 2T, so the argument below is pi * t_AC / (2T).
    public static double Amplitude(double liftAngle, double t1, double bph)
    {
        return liftAngle / (2.0 * Math.Sin((Math.PI * t1) / (7200.0 / bph)));
    }

    private bool ComputeRateError(double eventSample, bool haveValidBph, double bph, WatchMetricsUpdate update)
    {
        // The shipped pipeline never delivers haveValidBph=false after the first
        // lock: DetectorMetricsEngine suppresses pre-sync events and TgDetector
        // drops the whole batch in which sync is lost. A re-lock at a different
        // BPH therefore arrives with the segment still anchored to the old
        // watch, so treat the BPH change itself as the segment restart.
        if (haveValidBph && _haveStartTime && (int)bph != _bph)
        {
            _haveStartTime = false;
        }

        if ((!haveValidBph) && (_haveStartTime))
        {
            _haveStartTime = false;
            _bphValid = false;
            ResetGraphRate();
            ResetRateAveraging(0.0);
            ResetDisplayAveraging(0.0);
        }
        else if ((haveValidBph) && (!_haveStartTime))
        {
            _haveStartTime = true;
            _ticTocBeatNumber = 0;
            _bphValid = true;
            _bph = (int)bph;
            _startTime = eventSample / (double)_config.SampleRate;
            _haveZeroOffset = false;
            _zeroOffsetValue = 0.0;
            _beatsPerSecondWindow = BeatWindowSize(_bph, seconds: 1);
            int rateWindow = Math.Max(GraphRateWarmupPoints, AveragingPeriodS() * _beatsPerSecondWindow);
            _graphRlsTicRate.Resize(rateWindow);
            _graphRlsTocRate.Resize(rateWindow);
            // Title-bar rolling means span the same averaging period. Amplitude pairs and
            // clean beat-error windows each yield ~one sample per two beats, so the window
            // holds about half as many samples as the per-beat rate window.
            int displayAverageWindow = Math.Max(1, AveragingPeriodS() * _beatsPerSecondWindow / 2);
            _titleAmplitudeDeg.Resize(displayAverageWindow);
            _titleBeatErrorMs.Resize(displayAverageWindow);
            ResetGraphRate();
            ResetRateAveraging(_startTime);
            ResetDisplayAveraging(_startTime);

            // Restart the beat-error window with the new sync segment too: a stale
            // _beatErrorTimes[0] from before the re-lock would otherwise let a
            // boundary interval that happens to pass IsSingleBeatInterval at the
            // new BPH validate a false signed beat error spanning two watches.
            _beatErrorIdx = 0;
            // A tic amplitude staged before the sync loss must not pair with the
            // first toc after re-lock. Reset() clears this on a full reset, but the
            // inline re-sync path here did not, so clear the staged tic on (re)sync.
            _amplitudeTicValid = false;

            // Derived measures restart with the sync segment: a stale _lastAEvent
            // from before a sync loss must not contribute a bogus period delta.
            _rollPeriodDelta.Resize(BeatWindowSize(_bph, DiffPeriodWindowSeconds));
            ResetDerivedMeasures();

            // The A-C interval acceptance ring holds timings tied to the previous
            // watch's BPH/amplitude. A re-lock to a different watch must not gate the
            // new segment's first A-C intervals against the stale median, or they are
            // rejected as outliers. Reset() clears these on a full reset; the inline
            // re-sync path must do the same.
            _recentAcHead = 0;
            _recentAcCount = 0;
        }

        if ((haveValidBph) && (_haveStartTime))
        {
            double instTimingError;
            double instTimingErrorMs;
            double expectedTimeTarget;
            double timeMeasured;

            timeMeasured = eventSample / (double)_config.SampleRate;
            // Original: 3600.0f / bph. bph is double, so the float literal 3600.0f is
            // promoted to double (== 3600.0 exactly) and the division is done in double;
            // there is no narrowing of the result.
            expectedTimeTarget = 3600.0 / bph;

            // Classify the A-to-A interval (re-anchoring the beat counter across
            // any detection gap) before the parity read and the expected-time
            // computation below consume the counter.
            bool gapDetected = AccumulatePeriodDelta(eventSample, expectedTimeTarget, out bool extraEvent);
            if (extraEvent)
            {
                return false;
            }

            _ticTocBeatNumber++;

            int ticOrToc = CurrentBeatPhase();

            instTimingError = (_startTime + _ticTocBeatNumber * expectedTimeTarget) - timeMeasured;
            instTimingErrorMs = instTimingError * 1000.00;
            if (!_haveZeroOffset)
            {
                _haveZeroOffset = true;
                _zeroOffsetValue = -instTimingErrorMs;
            }
            instTimingErrorMs = instTimingErrorMs + _zeroOffsetValue;
            _lastRateErrorMs = instTimingErrorMs;

            if (gapDetected)
            {
                // A staged tic amplitude belongs to the pre-gap schedule; the
                // gap re-anchors tic/toc parity, so the gap-ending event can be
                // a toc that would otherwise pair the stale pre-gap tic with a
                // post-gap toc into a bogus pair average. Drop it, mirroring the
                // re-sync clear and the rate-estimator restart above.
                _amplitudeTicValid = false;
                ResetGraphRate();
                ResetDisplayAveraging(timeMeasured);
            }

            double wrappedRateError = WrapIntoRange(
                instTimingErrorMs,
                -_config.RateErrorYScale,
                _config.RateErrorYScale);

            if (ticOrToc == Tic)
            {
                _graphRlsTicRate.AddPoint(timeMeasured, instTimingError);
                AddRatePoint(_xTic, _yTic, wrappedRateError, _config.MaxRateDataPoints, ref _xTicIndex);
                update.SetTicRate(_xTic, _yTic);
            }
            else
            {
                _graphRlsTocRate.AddPoint(timeMeasured, instTimingError);
                AddRatePoint(_xToc, _yToc, wrappedRateError, _config.MaxRateDataPoints, ref _xTocIndex);
                update.SetTocRate(_xToc, _yToc);
            }

            UpdateGraphRate(ticOrToc);
            AccumulateRateAverage(ticOrToc, timeMeasured, expectedTimeTarget, gapDetected, update);
        }

        return true;
    }

    private int AveragingPeriodS()
    {
        return Math.Max(1, _config.AveragingPeriod);
    }

    /// <summary>
    /// Accumulates measured-vs-expected beat-duration deltas (consecutive A events)
    /// for DiffPeriod / AvgPeriod. Intervals off by more than half a beat span a
    /// detection gap rather than a single beat and would poison the averages, so
    /// they are excluded; gaps additionally re-anchor the tic/toc beat counter to
    /// the physical schedule, so this must run before the counter is advanced for
    /// the current event. Returns true when the interval spans a detection gap.
    /// </summary>
    private bool AccumulatePeriodDelta(double eventSample, double expectedTimeTarget, out bool extraEvent)
    {
        bool gapDetected = false;
        extraEvent = false;
        if (_haveAEvent && !_skipNextPeriodDelta)
        {
            double measuredPeriodS = (eventSample - _lastAEvent) / (double)_config.SampleRate;
            double deltaMs = (measuredPeriodS - expectedTimeTarget) * 1000.0;
            if (Math.Abs(deltaMs) < expectedTimeTarget * 500.0)
            {
                _rollPeriodDelta.Add(deltaMs);
                _avgPeriodDeltaSumMs += deltaMs;
                _avgPeriodDeltaCount++;
            }
            else if (deltaMs > 0.0)
            {
                // An over-long interval means the detector skipped beats; the
                // interval covers ~N nominal beats, of which N-1 went undetected.
                int beatsSpanned = QRound(measuredPeriodS / expectedTimeTarget);
                ulong skippedBeats = (ulong)Math.Max(1, beatsSpanned - 1);
                _missedBeats += skippedBeats;
                // Advance the beat counter past the undetected beats: its parity
                // is the tic/toc label and its product with the nominal period is
                // the expected-time schedule, so leaving it behind sign-inverts
                // every signed beat error / DiffTicTac after an odd-length gap
                // (and mispairs the amplitude average), while every gap shifts
                // the Error Rate baseline by a full beat per missed beat.
                _ticTocBeatNumber += skippedBeats;
                gapDetected = true;
            }
            else
            {
                extraEvent = true;
            }
        }

        if (!extraEvent)
        {
            _skipNextPeriodDelta = false;
        }
        return gapDetected;
    }

    private static int BeatWindowSize(int bph, int seconds)
    {
        return Math.Max(1, (int)Math.Round(bph * seconds / 3600.0, MidpointRounding.AwayFromZero));
    }

    private void UpdateGraphRate(int ticOrToc)
    {
        if (ticOrToc != Toc)
        {
            return;
        }

        if ((_graphRlsTicRate.Count >= GraphRateWarmupPoints) &&
            (_graphRlsTocRate.Count >= GraphRateWarmupPoints) &&
            (_graphRlsTicRate.GetRate(out double slopeTic)) &&
            (_graphRlsTocRate.GetRate(out double slopeToc)))
        {
            _graphRateSPerDay = ((slopeTic * 86400.0) + (slopeToc * 86400.0)) / 2.0;
            _graphRateValid = true;
        }
        else
        {
            _graphRateValid = false;
        }
    }

    private void AccumulateRateAverage(
        int ticOrToc,
        double timeMeasured,
        double expectedTimeTarget,
        bool gapDetected,
        WatchMetricsUpdate update)
    {
        if (gapDetected)
        {
            ResetRateAveraging(timeMeasured);
        }

        double nominalSamePhasePeriodS = expectedTimeTarget * 2.0;
        if (_haveRatePhaseEvent[ticOrToc])
        {
            double measuredSamePhasePeriodS = timeMeasured - _lastRatePhaseEventS[ticOrToc];
            double deltaS = measuredSamePhasePeriodS - nominalSamePhasePeriodS;
            if (Math.Abs(deltaS) < nominalSamePhasePeriodS * 0.5)
            {
                _rateWindowDeltaSumS += deltaS;
                _rateWindowDeltaCount++;
            }
        }

        _lastRatePhaseEventS[ticOrToc] = timeMeasured;
        _haveRatePhaseEvent[ticOrToc] = true;
        CompleteRateAverageWindows(timeMeasured, nominalSamePhasePeriodS, update);
    }

    private void CompleteRateAverageWindows(
        double timeMeasured,
        double nominalSamePhasePeriodS,
        WatchMetricsUpdate update)
    {
        int periodS = AveragingPeriodS();
        if (timeMeasured - _rateWindowStartS < periodS)
        {
            return;
        }

        double intervalStartS = _rateWindowStartS;
        double intervalStartGraphX = _rateWindowStartGraphX;
        double intervalEndGraphX = CurrentRateGraphX();

        if (_rateWindowDeltaCount > 0)
        {
            double averageDeltaS = _rateWindowDeltaSumS / _rateWindowDeltaCount;
            // Divide by the MEASURED same-phase period (nominal + delta), not the
            // nominal one: s/day error is elapsed-real-time relative, so the
            // denominator is the actual period the watch ran. Using the nominal
            // period understates the rate by 1/(1+r/86400) and disagrees with the
            // per-beat RLS rate and the simulator's reciprocal rate model at large
            // rates (e.g. +999 s/d would read +987.6).
            _displayRateSPerDay = -(averageDeltaS / (nominalSamePhasePeriodS + averageDeltaS)) * 86400.0;
            _lastAveragePeriodRateInterval = new AveragePeriodRateInterval(
                intervalStartGraphX,
                intervalEndGraphX,
                intervalStartS,
                intervalStartS + periodS,
                _displayRateSPerDay,
                _displayAmplitudeValid,
                _displayAmplitudeDeg,
                _displayBeatErrorValid,
                _displayBeatErrorMs);
            _haveAveragePeriodRateInterval = true;
            update.SetAveragePeriodRateInterval(_lastAveragePeriodRateInterval);
        }
        else
        {
            _displayRateSPerDay = 0.0;
        }

        do
        {
            _rateWindowStartS += periodS;
        }
        while (timeMeasured - _rateWindowStartS >= periodS);

        _rateWindowStartGraphX = intervalEndGraphX;
        ResetRateWindow();
    }

    private double CurrentRateGraphX() => Math.Max(_xTicIndex, _xTocIndex);

    /// <summary>
    /// True when the A-to-A interval is within half a nominal beat of the locked
    /// beat period, i.e. it represents exactly one beat (no detection gap, no
    /// spurious extra event). Callable only while _bphValid.
    /// </summary>
    private bool IsSingleBeatInterval(double intervalS)
    {
        double expectedS = 3600.0 / _bph;
        return Math.Abs(intervalS - expectedS) < expectedS * 0.5;
    }

    private void ComputeBeatError(double eventSample, WatchMetricsUpdate update)
    {
        bool displayAverageCompleted = false;
        _beatErrorTimes[_beatErrorIdx] = eventSample;
        _beatErrorIdx++;
        if (_beatErrorIdx == 3)
        {
            double t1 = (_beatErrorTimes[1] - _beatErrorTimes[0]) / (double)_config.SampleRate;
            double t2 = (_beatErrorTimes[2] - _beatErrorTimes[1]) / (double)_config.SampleRate;

            _beatErrorMs = Math.Abs(((t1 - t2) / 2.0) * 1000.0);

            if (_haveStartTime && IsSingleBeatInterval(t1) && IsSingleBeatInterval(t2))
            {
                displayAverageCompleted |= AccumulateDisplayBeatErrorAverage(
                    eventSample / (double)_config.SampleRate,
                    _beatErrorMs);
                // Title-bar rolling mean: same clean-window sample, updated every beat.
                _titleBeatErrorMs.Add(_beatErrorMs);
                // The window start's phase equals the current event's phase (the
                // window advances two beats per completion), so a tic-start window
                // makes t1 the tick duration and t2 the tock duration; normalize
                // DiffTicTac to (tick - tock) regardless of the start phase.
                double diffMs = (t1 - t2) * 1000.0;
                _diffTicTacMs = CurrentBeatPhase() == Tic ? diffMs : -diffMs;
                _diffTicTacValid = true;
                _signedBeatErrorMs = _diffTicTacMs / 2.0;
                _signedBeatErrorValid = true;
            }
            else if (_haveStartTime)
            {
                // A window interval spanning a detection gap (or a spurious extra
                // event) is not a tick/tock duration; a single missed beat would
                // otherwise inject a half-beat-sized fake error (~62.5 ms at
                // 28800 BPH) into the cumulative history and position statistics.
                // Same half-beat criterion as AccumulatePeriodDelta; the signed
                // values stay invalid until the next clean window (two beats).
                _diffTicTacValid = false;
                _signedBeatErrorValid = false;
            }

            _beatErrorTimes[0] = _beatErrorTimes[2];
            _beatErrorIdx = 1;
        }

        if (_haveStartTime)
        {
            displayAverageCompleted |= CompleteDisplayBeatErrorWindows(eventSample / (double)_config.SampleRate);
        }

        if (displayAverageCompleted)
        {
            RefreshAveragePeriodRateInterval(update);
        }
    }

    private bool AccumulateDisplayBeatErrorAverage(double eventTimeS, double beatErrorMs)
    {
        _displayBeatErrorWindowSumMs += beatErrorMs;
        _displayBeatErrorWindowCount++;
        return CompleteDisplayBeatErrorWindows(eventTimeS);
    }

    private bool CompleteDisplayBeatErrorWindows(double eventTimeS)
    {
        int periodS = AveragingPeriodS();
        if (eventTimeS - _displayBeatErrorWindowStartS < periodS)
        {
            return false;
        }

        if (_displayBeatErrorWindowCount > 0)
        {
            _displayBeatErrorMs = _displayBeatErrorWindowSumMs / _displayBeatErrorWindowCount;
            _displayBeatErrorValid = true;
        }
        else
        {
            _displayBeatErrorMs = 0.0;
            _displayBeatErrorValid = false;
        }

        do
        {
            _displayBeatErrorWindowStartS += periodS;
        }
        while (eventTimeS - _displayBeatErrorWindowStartS >= periodS);

        ResetDisplayBeatErrorWindow();
        return true;
    }

    private void ComputeAmplitude(double eventSample, double bph, WatchMetricsUpdate update)
    {
        bool displayAverageCompleted = false;
        double eventTimeS = eventSample / (double)_config.SampleRate;
        _lastAmplitudeInstValid = false;
        _lastAmplitudePairUpdated = false;

        if ((_haveAEvent) && (_bphValid))
        {
            int ticOrToc = CurrentBeatPhase();
            // Compensate the detector's A-onset latency so the A->C interval reflects
            // the true onset-to-C span the amplitude model expects (see Amplitude()).
            double time = (eventSample - _lastAEvent) / (double)_config.SampleRate
                          + _config.AmplitudeOnsetLatencyS;
            if (!AcceptAcInterval(time * 1000.0))
            {
                if (ticOrToc == Tic)
                {
                    _amplitudeTicValid = false;
                }
                if (CompleteDisplayAmplitudeWindows(eventTimeS))
                {
                    RefreshAveragePeriodRateInterval(update);
                }
                return;
            }
            double tempAmp = Amplitude(_config.LiftAngle, time, bph);
            // Valid amplitude is a positive angle below 360 deg. A mispaired or
            // delayed C can push t_AC past the half-cycle so Sin() goes negative
            // and the formula yields a negative (or non-finite) amplitude; reject
            // those instead of treating them as a real measurement.
            if (tempAmp > 0.0 && tempAmp < 360.00)
            {
                _lastAmplitudeInstValid = true;
                _lastAmplitudeInstDeg = tempAmp;

                if (ticOrToc == Tic)
                {
                    _amplitudeTicValid = true;
                    _amplitudeTic = tempAmp;
                }
                else
                {
                    _amplitudeToc = tempAmp;
                    if (_amplitudeTicValid)
                    {
                        double averageAmplitudeTicToc = (_amplitudeTic + _amplitudeToc) / 2.0;
                        _amplitudeTicValid = false;
                        _lastAmplitudePairUpdated = true;
                        _lastAmplitudePairDeg = averageAmplitudeTicToc;
                        displayAverageCompleted |= AccumulateDisplayAmplitudeAverage(
                            eventTimeS,
                            averageAmplitudeTicToc);
                        // Title-bar rolling mean: same paired sample, updated every beat.
                        _titleAmplitudeDeg.Add(averageAmplitudeTicToc);
                    }
                }
            }
            else if (ticOrToc == Tic)
            {
                _amplitudeTicValid = false;
            }

            displayAverageCompleted |= CompleteDisplayAmplitudeWindows(eventTimeS);
        }

        if (displayAverageCompleted)
        {
            RefreshAveragePeriodRateInterval(update);
        }
    }

    private bool AccumulateDisplayAmplitudeAverage(double eventTimeS, double amplitudeDeg)
    {
        _displayAmplitudeWindowSumDeg += amplitudeDeg;
        _displayAmplitudeWindowCount++;
        return CompleteDisplayAmplitudeWindows(eventTimeS);
    }

    private bool CompleteDisplayAmplitudeWindows(double eventTimeS)
    {
        int periodS = AveragingPeriodS();
        if (eventTimeS - _displayAmplitudeWindowStartS < periodS)
        {
            return false;
        }

        if (_displayAmplitudeWindowCount > 0)
        {
            _displayAmplitudeDeg = _displayAmplitudeWindowSumDeg / _displayAmplitudeWindowCount;
            _displayAmplitudeValid = true;
        }
        else
        {
            _displayAmplitudeDeg = 0.0;
            _displayAmplitudeValid = false;
        }

        do
        {
            _displayAmplitudeWindowStartS += periodS;
        }
        while (eventTimeS - _displayAmplitudeWindowStartS >= periodS);

        ResetDisplayAmplitudeWindow();
        return true;
    }

    private void RefreshAveragePeriodRateInterval(WatchMetricsUpdate update)
    {
        if (!_haveAveragePeriodRateInterval)
        {
            return;
        }

        // Only refresh the interval whose averaging period the just-completed display
        // window actually belongs to. When a period boundary falls between an A and
        // its C, the amplitude window can complete (advancing _displayAmplitudeWindowStartS
        // to that window's end) BEFORE the matching rate window is emitted; the last
        // emitted interval is then the PREVIOUS period. Without this guard the new
        // period's amplitude/beat-error would overwrite the previous interval. The
        // window end equals the matching interval's EndTimeS, so a difference of a
        // whole period means this completion belongs to a later, not-yet-emitted
        // interval (which captures these values at creation instead).
        if (_displayAmplitudeWindowStartS - _lastAveragePeriodRateInterval.EndTimeS > AveragingPeriodS() * 0.5)
        {
            return;
        }

        _lastAveragePeriodRateInterval = _lastAveragePeriodRateInterval with
        {
            AmplitudeValid = _displayAmplitudeValid,
            AmplitudeDeg = _displayAmplitudeDeg,
            BeatErrorValid = _displayBeatErrorValid,
            BeatErrorMs = _displayBeatErrorMs,
        };
        update.SetAveragePeriodRateInterval(_lastAveragePeriodRateInterval);
    }

    private bool AcceptAcInterval(double acMs)
    {
        bool accepted = true;
        if (_recentAcCount >= 4)
        {
            double median = Median(_recentAcMs, _recentAcCount);
            double mad = MedianAbsoluteDeviation(_recentAcMs, _recentAcCount, median);
            double threshold = Math.Max(AcDeviationFloorMs, AcDeviationMadScale * mad);
            accepted = Math.Abs(acMs - median) <= threshold;
        }

        // Every interval feeds the median/MAD baseline, accepted or not: this is the
        // adaptation path, not just an outlier filter. The A-C interval legitimately
        // shifts when the watch amplitude changes (a larger swing moves the C peak),
        // which is NOT a segment/BPH change and so does not reset the ring; pushing
        // unconditionally lets a sustained shift pull the median across so the new
        // amplitude is tracked, while the MAD threshold still rejects a lone outlier
        // for the current beat. (Verified by DisplayReadouts_UseCompletedAvgPeriod*,
        // which drives a 52deg->104deg amplitude change through this path.)
        _recentAcMs[_recentAcHead] = acMs;
        _recentAcHead = (_recentAcHead + 1) % _recentAcMs.Length;
        if (_recentAcCount < _recentAcMs.Length)
        {
            _recentAcCount++;
        }

        return accepted;
    }

    private static double Median(double[] ring, int count)
    {
        Span<double> values = stackalloc double[AcHistoryCount];
        for (int i = 0; i < count; i++)
        {
            values[i] = ring[i];
        }

        values = values[..count];
        values.Sort();
        int mid = count / 2;
        return (count & 1) != 0 ? values[mid] : 0.5 * (values[mid - 1] + values[mid]);
    }

    private static double MedianAbsoluteDeviation(double[] ring, int count, double median)
    {
        Span<double> values = stackalloc double[AcHistoryCount];
        for (int i = 0; i < count; i++)
        {
            values[i] = Math.Abs(ring[i] - median);
        }

        values = values[..count];
        values.Sort();
        int mid = count / 2;
        return (count & 1) != 0 ? values[mid] : 0.5 * (values[mid - 1] + values[mid]);
    }

    private string FormatCMarkerText(double beatTimeSeconds, bool haveValidBph, double bph)
    {
        if ((haveValidBph) && (_bphValid) && (_haveAEvent))
        {
            // Amplitude uses the latency-compensated A->C span (matching ComputeAmplitude);
            // the displayed "ms" below stays the raw measured interval.
            int amplitudeDeg = QRound(Amplitude(
                _config.LiftAngle, beatTimeSeconds + _config.AmplitudeOnsetLatencyS, bph));
            if (amplitudeDeg > 0 && amplitudeDeg < 360)
            {
                // " %1 ms\n%2%3" : ms (f,1), amplitude degrees (int), degree sign (U+00B0)
                return " " + FormatFixed(beatTimeSeconds * 1000.0, 1) + " ms\n"
                       + amplitudeDeg.ToString(CultureInfo.InvariantCulture) + "°";
            }
        }

        // " %1 ms " : ms (f,1)
        return " " + FormatFixed(beatTimeSeconds * 1000.0, 1) + " ms ";
    }

    // The title-bar readout wraps each live numeric value in these markers so the UI can color
    // only the numbers (not the labels or dash placeholders). Braces never occur in the values
    // or labels themselves, and the UI strips them before display.
    public const char ValueSpanStart = '{';
    public const char ValueSpanEnd = '}';

    private static string Mark(string value) => ValueSpanStart + value + ValueSpanEnd;

    // All three title-bar live values now refresh every beat. Error Rate uses the
    // rolling least-squares graph rate (_graphRateSPerDay, the single RateSPerDay
    // carried in BeatTimingSample and shared across every other view). Amplitude and
    // beat error use per-beat rolling means (_titleAmplitudeDeg / _titleBeatErrorMs)
    // instead of the completed-averaging-period block values, so they update smoothly
    // with the rate rather than stepping once per period. Title-bar only: these rolling
    // means intentionally diverge from the avg-period amplitude/beat-error the Long-Term /
    // Waveform-Compare views and the measurement CSV still show.
    private string FormatResults() => BuildResults(
        _bphValid, _bph,
        _graphRateValid, _graphRateSPerDay,
        _titleBeatErrorMs.CurrentSize() > 0, _titleBeatErrorMs.GetAverage(),
        _titleAmplitudeDeg.CurrentSize() > 0, _titleAmplitudeDeg.GetAverage());

    /// <summary>
    /// Pure formatter for the title-bar readout. Each field is fixed-width so the line never
    /// shifts as values change; present numeric values are wrapped in value-span markers (so
    /// the UI can accent only the numbers) while dash placeholders are left unmarked. Widths:
    /// Error Rate 6 ("-999.9"), amplitude 3 + constant degree sign, beat error 4 ("-9.9"), BPH 5.
    /// </summary>
    internal static string BuildResults(
        bool bphValid, int bph,
        bool rateValid, double rate,
        bool beatErrorValid, double beatError,
        bool amplitudeValid, double amplitude)
    {
        string beatsPerHour = bphValid ? Mark(ArgInt(bph, 5)) : "-----";
        string rateError = rateValid ? Mark(PrintfPlusFloat(rate, 6, 1)) : "------";
        string beatErrorText = beatErrorValid ? Mark(ArgFixed(beatError, 4, 1)) : "----";
        string amplitudeText = amplitudeValid ? Mark(ArgLong(QRound64(amplitude), 3)) : "---";

        return "Error Rate " + rateError + " s/d | Amplitude " + amplitudeText + "°" +
               " | Beat Error " + beatErrorText + " ms | BPH " + beatsPerHour;
    }

    // MainWindow::WrapInToRange: fmod into the range, adding the range size
    // when the remainder is negative (C# '%' on doubles == C fmod).
    private double WrapIntoRange(double number, double lowerBound, double upperBound)
    {
        double rangeWidth = upperBound - lowerBound;
        double wrapped = (number - lowerBound) % rangeWidth;
        if (wrapped < 0)
        {
            wrapped += rangeWidth;
        }

        return wrapped + lowerBound;
    }

    // Appends the newest rate-error point and keeps only the latest maxSize, so the
    // plot shows a scrolling window of the most recent beats. X is the absolute beat
    // index (monotonically increasing, never wrapped), so the renderer can follow the
    // newest window and older points scroll off the left edge. The old ring-overwrite
    // kept the buffer pinned at maxSize and mutated existing slots in place, so past
    // ~250 beats no new point ever appeared and the plot froze as a static cloud.
    private void AddRatePoint(List<double> xvec, List<double> yvec, double value, int maxSize, ref int beatIndex)
    {
        xvec.Add(beatIndex);
        yvec.Add(value);
        beatIndex++;

        if (xvec.Count > maxSize)
        {
            int excess = xvec.Count - maxSize;
            xvec.RemoveRange(0, excess);
            yvec.RemoveRange(0, excess);
        }
    }

    // --- Formatting / rounding helpers (Qt-compatible) ---

    /// <summary>qRound(double): round half away from zero, returning int (matches Qt).</summary>
    private static int QRound(double d)
    {
        return d >= 0.0 ? (int)(d + 0.5) : (int)(d - 0.5);
    }

    /// <summary>qRound64(double): round half away from zero, returning long (matches Qt).</summary>
    private static long QRound64(double d)
    {
        return d >= 0.0 ? (long)(d + 0.5) : (long)(d - 0.5);
    }

    /// <summary>Fixed-point format with given decimals, like QString::arg(v,0,'f',prec).</summary>
    private static string FormatFixed(double v, int decimals)
    {
        return v.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>QString::arg(int, fieldWidth, base 10, ' '): right-aligned, space-padded.</summary>
    private static string ArgInt(int value, int fieldWidth)
    {
        string s = value.ToString(CultureInfo.InvariantCulture);
        return s.Length < fieldWidth ? s.PadLeft(fieldWidth, ' ') : s;
    }

    /// <summary>QString::arg(qint64, fieldWidth, base 10, ' '): right-aligned, space-padded.</summary>
    private static string ArgLong(long value, int fieldWidth)
    {
        string s = value.ToString(CultureInfo.InvariantCulture);
        return s.Length < fieldWidth ? s.PadLeft(fieldWidth, ' ') : s;
    }

    /// <summary>QString::arg(double, fieldWidth, 'f', prec): fixed prec, right-aligned, space-padded.</summary>
    private static string ArgFixed(double value, int fieldWidth, int decimals)
    {
        string s = FormatFixed(value, decimals);
        return s.Length < fieldWidth ? s.PadLeft(fieldWidth, ' ') : s;
    }

    /// <summary>C printf "%+W.Pf": forced sign, fixed P decimals, right-aligned in width W (space pad).</summary>
    private static string PrintfPlusFloat(double value, int width, int decimals)
    {
        // Format magnitude with fixed decimals, then prepend the explicit sign.
        string sign = value < 0.0 ? "-" : "+";
        string body = Math.Abs(value).ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        string s = sign + body;
        return s.Length < width ? s.PadLeft(width, ' ') : s;
    }
}
