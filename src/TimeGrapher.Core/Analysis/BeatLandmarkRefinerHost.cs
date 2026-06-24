using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;

namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Engine-level landmark-refiner configuration. Carries the implementation plus
/// the host-enforced safety policy: the per-landmark correction clamp windows
/// (relative to the detector peak, in ms) and the confidence floor below which a
/// proposed correction is ignored. Defaults match the plan
/// (docs/for-ai/TINYML_LANDMARK_REFINER_PLAN.md): A in [-8 ms, +2 ms], C in
/// [-4 ms, +6 ms].
/// </summary>
public sealed record BeatLandmarkRefinerConfig(
    IBeatLandmarkRefiner Refiner,
    double AClampPreMs = 8.0,
    double AClampPostMs = 2.0,
    double CClampPreMs = 4.0,
    double CClampPostMs = 6.0,
    float ConfidenceThreshold = 0.5f);

/// <summary>
/// Hosts an <see cref="IBeatLandmarkRefiner"/> at the metrics choke point of
/// <see cref="DetectorMetricsEngine"/>. Unlike the veto-only
/// <see cref="BeatEventGateHost"/>, the refiner re-times the C (and optionally
/// A) PEAK of a locked beat. It pairs each A with its following C into one
/// <see cref="BeatLandmarkCandidate"/>, calls
/// <see cref="IBeatLandmarkRefiner.Refine"/> once the beat's delayed-envelope
/// window is available, then enforces the safety policy the refiner cannot
/// bypass: every correction is clamped to a small window around the detector
/// peak and falls back to the detector value when the refiner declines
/// (<see cref="BeatLandmarkRefinement.Accepted"/> = false) or its confidence is
/// below threshold (fail-open).
///
/// <para>C-onset timing is rigid-shifted by the same delta as the corrected
/// peak, so the <c>UseCOnset</c> selection (done downstream on the corrected
/// event) tracks the correction without the host re-running onset detection -
/// the two can never double-correct.</para>
///
/// <para>The RAW detector stream is never altered: only the metrics/display
/// copies carry corrections. BPH detection and the sync PLL see the raw stream,
/// so the refiner cannot affect lock (structural guarantee).</para>
/// </summary>
internal sealed class BeatLandmarkRefinerHost
{
    private readonly IBeatLandmarkRefiner _refiner;
    private readonly double _sampleRate;
    private readonly int _preSamples;
    private readonly int _postSamples;
    private readonly bool _windowed;
    private readonly double _aClampPre, _aClampPost, _cClampPre, _cClampPost;
    private readonly float _confidenceThreshold;

    /* Delayed-envelope ring (windowed refiners only); identical sizing to the
     * gate host so a beat window is structurally guaranteed once released. */
    private readonly float[] _ring;
    private ulong _ringNewestAbs;
    private int _ringCount;
    private int _ringHead;
    private readonly float[] _windowScratch;

    internal readonly record struct RefinedEvent(TgEvent Event, bool Synced, int DetectedBph);

    private readonly record struct PendingEvent(
        TgEvent Event, bool Synced, int DetectedBph,
        double BeatPeriodS, float NoiseFloor, float ReferencePeak);

    /* FIFO of submitted events in detector order. A beat is formed from an A and
     * the C that immediately follows it; unpaired events pass through uncorrected. */
    private readonly List<PendingEvent> _pending = new();
    private readonly List<RefinedEvent> _released = new();

    public BeatLandmarkRefinerHost(BeatLandmarkRefinerConfig config, double sampleRate)
    {
        _refiner = config.Refiner;
        _sampleRate = sampleRate;
        _preSamples = (int)(_refiner.WindowPreMs * 1e-3 * sampleRate);
        _postSamples = (int)(_refiner.WindowPostMs * 1e-3 * sampleRate);
        _windowed = _preSamples > 0 || _postSamples > 0;
        _aClampPre = config.AClampPreMs * 1e-3 * sampleRate;
        _aClampPost = config.AClampPostMs * 1e-3 * sampleRate;
        _cClampPre = config.CClampPreMs * 1e-3 * sampleRate;
        _cClampPost = config.CClampPostMs * 1e-3 * sampleRate;
        _confidenceThreshold = config.ConfidenceThreshold;
        if (_windowed)
        {
            /* Out-size the request plus one second of block headroom, like the
             * gate host. The scratch must hold a whole beat window (A-pre .. C+post),
             * which is wider than a single-event window, so size it to the ring. */
            int capacity = Math.Max((int)(0.5 * sampleRate),
                                    _preSamples + _postSamples + 1 + (int)sampleRate);
            _ring = new float[capacity];
            _windowScratch = new float[capacity];
        }
        else
        {
            _ring = Array.Empty<float>();
            _windowScratch = Array.Empty<float>();
        }
    }

    /// <summary>Feeds the block's delayed envelope; no-op for zero-window refiners.</summary>
    public void AppendEnvelope(float[] pcm, int len, ulong startAbs)
    {
        if (!_windowed || len == 0)
        {
            return;
        }
        for (int i = 0; i < len; i++)
        {
            _ring[_ringHead] = pcm[i];
            _ringHead = (_ringHead + 1) % _ring.Length;
        }
        _ringNewestAbs = startAbs + (ulong)len - 1;
        _ringCount = Math.Min(_ring.Length, _ringCount + len);
    }

    public void Submit(in TgEvent ev, bool synced, int detectedBph,
                       double beatPeriodS, float noiseFloor, float referencePeak)
        => _pending.Add(new PendingEvent(ev, synced, detectedBph, beatPeriodS, noiseFloor, referencePeak));

    /// <summary>
    /// Refines and releases every pending beat whose window is available, in
    /// detector order. <paramref name="force"/> flushes the rest at stream/sync
    /// boundaries (a still-windowless beat is refined against whatever the ring
    /// holds; an unpaired A is released uncorrected). The list is reused.
    /// </summary>
    public List<RefinedEvent> Release(bool force)
    {
        _released.Clear();
        int consumed = 0;
        while (consumed < _pending.Count)
        {
            PendingEvent first = _pending[consumed];
            if (first.Event.Type == TgEventType.A)
            {
                if (consumed + 1 < _pending.Count)
                {
                    PendingEvent second = _pending[consumed + 1];
                    if (second.Event.Type == TgEventType.C)
                    {
                        if (!BeatReady(second.Event) && !force)
                        {
                            break;
                        }
                        RefineBeat(first, second);
                        consumed += 2;
                        continue;
                    }

                    // Next event is another A: the first A had no C - pass it through.
                    _released.Add(new RefinedEvent(first.Event, first.Synced, first.DetectedBph));
                    consumed += 1;
                    continue;
                }

                // A is the last pending event: its C may still arrive next block.
                if (!force)
                {
                    break;
                }
                _released.Add(new RefinedEvent(first.Event, first.Synced, first.DetectedBph));
                consumed += 1;
                continue;
            }

            // Orphan C with no preceding A: nothing to refine, pass it through.
            _released.Add(new RefinedEvent(first.Event, first.Synced, first.DetectedBph));
            consumed += 1;
        }
        _pending.RemoveRange(0, consumed);
        return _released;
    }

    /// <summary>Forwards sync-loss / regime-reset notifications to the refiner.</summary>
    public void ResetRefiner() => _refiner.Reset();

    private bool BeatReady(in TgEvent cEvent)
        => !_windowed || _ringNewestAbs >= cEvent.SampleIndex + (ulong)_postSamples;

    private void RefineBeat(in PendingEvent a, in PendingEvent c)
    {
        double aSample = a.Event.SampleIndex + a.Event.SubSampleOffset;
        double cSample = c.Event.SampleIndex + c.Event.SubSampleOffset;
        var candidate = new BeatLandmarkCandidate(
            a.Event, c.Event, aSample, cSample,
            c.Synced, c.DetectedBph, c.BeatPeriodS, c.NoiseFloor, c.ReferencePeak);

        BeatLandmarkRefinement r = Refine(candidate, a.Event, c.Event);

        TgEvent aOut = a.Event;
        TgEvent cOut = c.Event;
        if (r.Accepted)
        {
            if (r.CorrectedC && r.CConfidence >= _confidenceThreshold)
            {
                double corrected = Clamp(r.CorrectedCSample, cSample - _cClampPre, cSample + _cClampPost);
                cOut = WithCorrectedPeak(c.Event, corrected, shiftOnset: true);
            }
            if (r.CorrectedA && r.AConfidence >= _confidenceThreshold)
            {
                double corrected = Clamp(r.CorrectedASample, aSample - _aClampPre, aSample + _aClampPost);
                aOut = WithCorrectedPeak(a.Event, corrected, shiftOnset: false);
            }
        }

        _released.Add(new RefinedEvent(aOut, a.Synced, a.DetectedBph));
        _released.Add(new RefinedEvent(cOut, c.Synced, c.DetectedBph));
    }

    private BeatLandmarkRefinement Refine(in BeatLandmarkCandidate candidate, in TgEvent aEv, in TgEvent cEv)
    {
        if (!_windowed || _ringCount == 0)
        {
            return _refiner.Refine(ReadOnlySpan<float>.Empty, -1, -1, _sampleRate, candidate);
        }

        ulong aIdx = aEv.SampleIndex;
        ulong cIdx = cEv.SampleIndex;
        ulong ringOldest = _ringNewestAbs - (ulong)(_ringCount - 1);
        ulong start = aIdx >= (ulong)_preSamples ? aIdx - (ulong)_preSamples : 0;
        if (start < ringOldest) start = ringOldest;
        ulong end = cIdx + (ulong)_postSamples;
        if (end > _ringNewestAbs) end = _ringNewestAbs;
        if (end < start)
        {
            return _refiner.Refine(ReadOnlySpan<float>.Empty, -1, -1, _sampleRate, candidate);
        }

        int len = (int)(end - start + 1);
        if (len > _windowScratch.Length) len = _windowScratch.Length;
        for (int i = 0; i < len; i++)
        {
            _windowScratch[i] = RingAt(start + (ulong)i);
        }
        int aOff = (aIdx >= start && aIdx < start + (ulong)len) ? (int)(aIdx - start) : -1;
        int cOff = (cIdx >= start && cIdx < start + (ulong)len) ? (int)(cIdx - start) : -1;
        return _refiner.Refine(_windowScratch.AsSpan(0, len), aOff, cOff, _sampleRate, candidate);
    }

    private TgEvent WithCorrectedPeak(TgEvent ev, double correctedPeakSample, bool shiftOnset)
    {
        double delta = correctedPeakSample - (ev.SampleIndex + ev.SubSampleOffset);
        (ulong idx, double off) = Split(correctedPeakSample);
        ev.SampleIndex = idx;
        ev.SubSampleOffset = off;
        ev.TimeSeconds = correctedPeakSample / _sampleRate;
        if (shiftOnset && ev.OnsetValid)
        {
            double newOnset = (ev.OnsetSampleIndex + ev.OnsetSubSampleOffset) + delta;
            (ulong oidx, double ooff) = Split(newOnset);
            ev.OnsetSampleIndex = oidx;
            ev.OnsetSubSampleOffset = ooff;
            ev.OnsetTimeSeconds = newOnset / _sampleRate;
        }
        return ev;
    }

    /* Split a fractional absolute sample into the canonical integer index plus a
     * sub-sample offset in [-0.5, +0.5], matching the detector's convention. */
    private static (ulong Index, double Offset) Split(double sample)
    {
        if (sample < 0.0) sample = 0.0;
        long i = (long)Math.Round(sample, MidpointRounding.AwayFromZero);
        if (i < 0) i = 0;
        return ((ulong)i, sample - i);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private float RingAt(ulong abs)
    {
        ulong age = _ringNewestAbs - abs;
        int newestSlot = (_ringHead + _ring.Length - 1) % _ring.Length;
        int idx = (int)(((ulong)newestSlot + (ulong)_ring.Length - age) % (ulong)_ring.Length);
        return _ring[idx];
    }
}
