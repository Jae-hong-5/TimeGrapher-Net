using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Captures one decimated envelope window per detected A event — from
/// <see cref="PreEventMs"/> before the A to <see cref="WindowMs"/> total — and
/// publishes the last <see cref="SegmentRingCount"/> completed windows as an
/// immutable <see cref="BeatSegmentsSnapshot"/> (Beat Noise; shared
/// infrastructure for any beat-aligned waveform view).
///
/// When the worker also feeds the un-rectified input via <see cref="AppendRaw"/>,
/// each window additionally captures the real bipolar waveform as per-point
/// min/max (<see cref="BeatSegment.RawMin"/> / <see cref="BeatSegment.RawMax"/>)
/// on the same point grid as the envelope, so the escapement views can show the
/// actual raw signal instead of the negated envelope. The raw and envelope share
/// the absolute input-sample index domain (the detector's 50 ms envelope delay
/// only re-times the displayed envelope; <c>ProcessedPcmStartSample</c> re-labels
/// it back to input samples), so the same window start indexes both rings.
///
/// A fixed-size rolling envelope ring, sized from the sample rate and the
/// configured event-gate post-window at construction, lets a window span
/// detector block boundaries and the bounded post-gate display latency of a
/// windowed event gate: a window opens on its accepted A event and is filled
/// from the ring only once the envelope has advanced past the window end.
/// Windows overlap (beats are shorter than the window), so several can be
/// pending at once.
///
/// Segment buffers rotate through a fixed pool of <see cref="SegmentPoolCount"/>
/// float[<see cref="SegmentPoints"/>] arrays instead of allocating per beat
/// (the SoundPrintFrameProjector publish-pool pattern). Reuse is gated on
/// publication, not completion count: a buffer referenced by either of the two
/// most recently built snapshots — or still sitting in the completed ring — is
/// skipped by the acquire scan, because the UI renders at most one snapshot
/// behind the newest published one and a backlog catch-up pass can complete
/// many beats inside a single Project call (a completion-count margin alone
/// would let such a pass refill what a routed frame is still reading). Ring
/// (≤8) plus two snapshots (≤16) protect at most 24 of the 28 buffers, so the
/// scan always finds a free one.
///
/// The capture also drives the Scope 2 <see cref="BeatNoiseAverager"/>: the
/// first 20 ms of every window is decimated into a reused scratch trace and
/// accumulated into the phase-alternating lanes as soon as its envelope data
/// arrives.
///
/// Sibling of <see cref="ScopeRateFrameProjector"/>; Project/AppendSnapshot run
/// on the analysis thread only.
/// </summary>
public sealed class BeatSegmentCapture
{
    /// <summary>Window length (ms): covers the 400 ms maximum Beat Noise range.</summary>
    public const double WindowMs = 400.0;

    /// <summary>Pre-roll before the A event (ms), so the noise onset is visible.</summary>
    public const double PreEventMs = 5.0;

    /// <summary>Fixed decimated points per segment (0.25 ms/point at the 400 ms window).</summary>
    public const int SegmentPoints = 1600;

    public const double MsPerPoint = WindowMs / SegmentPoints;

    /// <summary>Completed segments kept (the strip lane shows this many recent beats).</summary>
    public const int SegmentRingCount = 8;

    /// <summary>
    /// Pooled segment buffers. Must exceed the worst-case protected set: the
    /// completed ring (8) plus the two most recently built snapshots (8 each,
    /// disjoint from the ring after a catch-up burst) = 24, leaving 4 free.
    /// </summary>
    public const int SegmentPoolCount = 28;

    private const double BaseEnvelopeRingSeconds = 0.6;

    /// <summary>
    /// The detector's fixed envelope delay line (TgDetector, 50 ms). The raw
    /// ring is NOT delayed, so it leads the envelope by this span and must hold
    /// that much extra history beyond the envelope ring or a completing window's
    /// start would have scrolled out of the raw ring before its raw is read.
    /// </summary>
    private const double DetectorEnvelopeDelayMs = 50.0;

    // Bounded backlog of open (not yet filled) windows; overflow drops the
    // oldest. A window completes WindowMs after it opens, but
    // CompleteReadySegments only runs once per delivery block, so completion
    // lags by up to one block: the true simultaneous-open bound is
    // ceil((WindowMs + block) / beat period), not ceil(WindowMs / beat period).
    // The ring is therefore sized in the constructor (see PendingSlotsFor) for
    // the fastest supported beat and the configured block, so a fast watch on a
    // large analysis block never evicts a still-completing window.
    private const double FastestSupportedBph = 43200.0;
    private const int MinPendingSlots = 8;

    private struct PendingSegment
    {
        public ulong StartSample;
        public double ASample;
        public float PeakValue;
        public bool IsTic;
        public bool HasC;
        public double CPeakSample;
        public bool COnsetValid;
        public double COnsetSample;
        public bool LaneAccumulated;
    }

    private struct CompletedSegment
    {
        public float[] Buffer;
        public int PoolIndex;
        public bool RawValid;
        public double StartTimeS;
        public bool IsTic;
        public double AOffsetMs;
        public float PeakValue;
        public bool CPeakValid;
        public double CPeakOffsetMs;
        public bool COnsetValid;
        public double COnsetOffsetMs;
    }

    private readonly int _sampleRate;
    private readonly double _liftAngleDeg;
    private readonly float[] _envelopeRing;
    private ulong _envelopeEndSample;

    // Parallel raw-input ring (un-rectified, bipolar). Filled by AppendRaw in
    // the same absolute index domain as the envelope ring; null-fed runs leave
    // _rawFed false and segments publish RawValid = false.
    private readonly float[] _rawRing;
    private ulong _rawEndSample;
    private bool _rawFed;

    private readonly float[][] _segmentPool;
    // Raw min/max segment buffers share the envelope pool's index, so the
    // existing publication-gated protection covers all three at once.
    private readonly float[][] _rawMinPool;
    private readonly float[][] _rawMaxPool;
    private int _nextPoolBuffer;

    // Snapshot version that last published each pool buffer (0 = never).
    // Buffers published within the last two built snapshots are protected
    // from reuse; see the class doc.
    private readonly ulong[] _bufferPublishedVersion = new ulong[SegmentPoolCount];

    private readonly PendingSegment[] _pending;
    private int _pendingHead;
    private int _pendingCount;
    private bool _lastIsTic;

    private readonly CompletedSegment[] _completed = new CompletedSegment[SegmentRingCount];
    private int _completedHead;
    private int _completedCount;
    private readonly List<BeatNoiseMarker> _markers = new();

    // Scope 2 lane averaging over the first LaneWindowMs of each window,
    // decimated into a reused scratch buffer (no per-beat allocation). The Σ
    // request is written from any thread (UI toggle) and applied analysis-side
    // at the start of the next pass (the SweepFrameProjector knob pattern).
    private readonly BeatNoiseAverager _averager = new();
    private readonly float[] _laneScratch = new float[BeatNoiseAverager.LanePoints];
    private volatile bool _requestedSigma;

    // Deadline-degradation knob (written by the worker's ladder): while set,
    // no new segment windows open.
    private volatile bool _captureSuspended;

    private bool _dirty;
    private ulong _version;
    private BeatSegmentsSnapshot? _snapshot;

    public BeatSegmentCapture(
        int sampleRate,
        double liftAngleDeg,
        double maxDisplayDelayMs = 0.0,
        int deliveryBlockSamples = 4096)
    {
        _sampleRate = sampleRate;
        _liftAngleDeg = liftAngleDeg;
        _pending = new PendingSegment[PendingSlotsFor(sampleRate, deliveryBlockSamples)];
        _envelopeRing = new float[EnvelopeRingLength(sampleRate, maxDisplayDelayMs, deliveryBlockSamples)];
        _rawRing = new float[RawRingLength(sampleRate, maxDisplayDelayMs, deliveryBlockSamples)];
        _segmentPool = new float[SegmentPoolCount][];
        _rawMinPool = new float[SegmentPoolCount][];
        _rawMaxPool = new float[SegmentPoolCount][];
        for (int i = 0; i < SegmentPoolCount; i++)
        {
            _segmentPool[i] = new float[SegmentPoints];
            _rawMinPool[i] = new float[SegmentPoints];
            _rawMaxPool[i] = new float[SegmentPoints];
        }
    }

    internal static int PendingSlotsFor(int sampleRate, int deliveryBlockSamples)
    {
        double blockMs = deliveryBlockSamples / (double)sampleRate * 1000.0;
        double fastestBeatMs = 3600.0 / FastestSupportedBph * 1000.0;
        // Worst case open windows = ceil((WindowMs + one delivery block) / fastest beat) + slack.
        int slots = (int)Math.Ceiling((WindowMs + blockMs) / fastestBeatMs) + 2;
        return Math.Max(MinPendingSlots, slots);
    }

    private static int EnvelopeRingLength(int sampleRate, double maxDisplayDelayMs, int deliveryBlockSamples)
    {
        int baseSamples = (int)Math.Ceiling(BaseEnvelopeRingSeconds * sampleRate);
        int delayedStartAgeSamples = (int)Math.Ceiling((maxDisplayDelayMs + PreEventMs) / 1000.0 * sampleRate) +
                                     deliveryBlockSamples + 1;
        return Math.Max(1, Math.Max(baseSamples, delayedStartAgeSamples));
    }

    private static int RawRingLength(int sampleRate, double maxDisplayDelayMs, int deliveryBlockSamples)
    {
        // The envelope ring already covers the window plus display-delay history;
        // the raw ring leads it by the detector's envelope delay, so add that
        // span (and a block of slack for the completion check firing a block late).
        int envelopeLength = EnvelopeRingLength(sampleRate, maxDisplayDelayMs, deliveryBlockSamples);
        int leadSamples = (int)Math.Ceiling(DetectorEnvelopeDelayMs / 1000.0 * sampleRate) + deliveryBlockSamples;
        return envelopeLength + leadSamples;
    }

    /// <summary>
    /// Requests the Scope 2 Σ averaging mode. Applied on the analysis thread at
    /// the start of the next pass; a change resets the averaging cycle.
    /// Callable from any thread.
    /// </summary>
    public void SetSigmaAveraging(bool enabled)
    {
        _requestedSigma = enabled;
    }

    public void Project(DetectorMetricsBlockUpdate update)
    {
        if (_averager.SetSigmaEnabled(_requestedSigma))
        {
            _dirty = true;
        }

        AppendEnvelope(update.Result);

        foreach (DetectedEventUpdate eventUpdate in update.DisplayEvents)
        {
            // Deadline-degradation: while suspended, the event stream is
            // ignored - no NEW windows open (already-open ones complete
            // naturally), shedding the per-beat window decimation and lane
            // accumulation; the Beat-Noise tab simply stops advancing until
            // pressure subsides. C events must be skipped too: a C whose own
            // beat's window was never opened would otherwise attach to an
            // older pending window missing its own C (AttachCEvent only
            // requires the A to precede the C) and render a wrong C-peak
            // marker once that window completes.
            if (_captureSuspended)
            {
                continue;
            }

            if (eventUpdate.Event.Type == TgEventType.A)
            {
                AddMarker(eventUpdate.EventSample, BeatNoiseMarkerKind.A);
                OpenSegment(eventUpdate);
            }
            else if (eventUpdate.Event.Type == TgEventType.C)
            {
                AddMarker(eventUpdate.Event.SampleIndex + eventUpdate.Event.SubSampleOffset, BeatNoiseMarkerKind.CPeak);
                if (eventUpdate.Event.OnsetValid)
                {
                    AddMarker(eventUpdate.Event.OnsetSampleIndex + eventUpdate.Event.OnsetSubSampleOffset, BeatNoiseMarkerKind.COnset);
                }

                AttachCEvent(eventUpdate.Event);
            }
        }

        // Lanes first: a window that is already fully complete in this pass
        // must still contribute its 20 ms lane trace before leaving the
        // pending queue.
        AccumulateReadyLanes();
        CompleteReadySegments();
    }

    /// <summary>
    /// Deadline-degradation knob: while suspended, no new segment windows open
    /// and stray C events are not attached to older pending windows.
    /// Thread-safe; applied on the next Project pass.
    /// </summary>
    public void SetCaptureSuspended(bool suspended)
    {
        _captureSuspended = suspended;
    }

    /// <summary>
    /// Latest snapshot, rebuilt only when a segment completed since the last
    /// build; in between, the same shared instance reattaches to every frame
    /// (the BeatMetricsHistory pattern — the per-beat rebuild allocates only
    /// the small segment descriptors, never the sample buffers).
    /// Null until the first completed segment.
    /// </summary>
    public void AppendSnapshot(AnalysisFrame frame)
    {
        frame.BeatSegments = CurrentSnapshot();
    }

    public BeatSegmentsSnapshot? CurrentSnapshot()
    {
        if (!_dirty)
        {
            return _snapshot;
        }

        _version++;
        var segments = new List<BeatSegment>(_completedCount);
        for (int i = 0; i < _completedCount; i++)
        {
            CompletedSegment completed = _completed[(_completedHead + i) % SegmentRingCount];
            _bufferPublishedVersion[completed.PoolIndex] = _version;
            segments.Add(new BeatSegment
            {
                Samples = completed.Buffer,
                RawValid = completed.RawValid,
                RawMin = completed.RawValid ? _rawMinPool[completed.PoolIndex] : ReadOnlyMemory<float>.Empty,
                RawMax = completed.RawValid ? _rawMaxPool[completed.PoolIndex] : ReadOnlyMemory<float>.Empty,
                MsPerPoint = MsPerPoint,
                StartTimeS = completed.StartTimeS,
                IsTic = completed.IsTic,
                AOffsetMs = completed.AOffsetMs,
                PeakValue = completed.PeakValue,
                CPeakValid = completed.CPeakValid,
                CPeakOffsetMs = completed.CPeakOffsetMs,
                COnsetValid = completed.COnsetValid,
                COnsetOffsetMs = completed.COnsetOffsetMs,
            });
        }

        IReadOnlyList<BeatNoiseMarker> markers = Array.Empty<BeatNoiseMarker>();
        if (segments.Count > 0)
        {
            double startS = segments[0].StartTimeS;
            double endS = segments[^1].StartTimeS + WindowMs / 1000.0;
            markers = _markers
                .Where(marker => marker.TimeS >= startS && marker.TimeS <= endS)
                .ToArray();
            _markers.RemoveAll(marker => marker.TimeS < startS);
        }

        _snapshot = new BeatSegmentsSnapshot
        {
            Version = _version,
            Segments = segments,
            Markers = markers,
            LiftAngleDeg = _liftAngleDeg,
            Average = _averager.Snapshot(),
        };
        _dirty = false;
        return _snapshot;
    }

    /// <summary>
    /// Appends one un-rectified raw input block to the raw ring, in the same
    /// absolute index domain as the envelope (the worker feeds the SAME block it
    /// hands the detector, in order, so the running count tracks the detector's
    /// input-sample clock). Must be called before <see cref="Project"/> for the
    /// matching block. Analysis thread only.
    /// </summary>
    public void AppendRaw(ReadOnlySpan<float> block)
    {
        int length = block.Length;
        if (length <= 0)
        {
            return;
        }

        _rawFed = true;

        // Wrap-aware block copy (at most two segments), mirroring AppendEnvelope.
        int ringLength = _rawRing.Length;
        ulong start = _rawEndSample;
        int copied = 0;
        while (copied < length)
        {
            int destination = (int)((start + (ulong)copied) % (ulong)ringLength);
            int chunk = Math.Min(length - copied, ringLength - destination);
            block.Slice(copied, chunk).CopyTo(_rawRing.AsSpan(destination, chunk));
            copied += chunk;
        }

        _rawEndSample = start + (ulong)length;
    }

    private void AppendEnvelope(DetectorResultSnapshot result)
    {
        int length = result.ProcessedPcmLen;
        if (length <= 0)
        {
            return;
        }

        // Wrap-aware block copy (at most two segments), not per-sample writes.
        ReadOnlySpan<float> processedPcm = result.ProcessedPcm.Span;
        int ringLength = _envelopeRing.Length;
        ulong start = result.ProcessedPcmStartSample;
        int copied = 0;
        while (copied < length)
        {
            int destination = (int)((start + (ulong)copied) % (ulong)ringLength);
            int chunk = Math.Min(length - copied, ringLength - destination);
            processedPcm.Slice(copied, chunk).CopyTo(_envelopeRing.AsSpan(destination, chunk));
            copied += chunk;
        }

        _envelopeEndSample = start + (ulong)length;
    }

    private void AddMarker(double sample, BeatNoiseMarkerKind kind)
    {
        _markers.Add(new BeatNoiseMarker
        {
            TimeS = sample / _sampleRate,
            Kind = kind,
        });
    }

    private void OpenSegment(DetectedEventUpdate eventUpdate)
    {
        double aSample = eventUpdate.EventSample;
        double preSamples = PreEventMs / 1000.0 * _sampleRate;
        ulong startSample = aSample > preSamples ? (ulong)(aSample - preSamples) : 0UL;

        // Phase from the metrics sample when it was emitted on this A event
        // (synced); otherwise keep the alternation going locally.
        bool isTic = eventUpdate.MetricsUpdate.BeatTimingSampleUpdated
            ? eventUpdate.MetricsUpdate.BeatTimingSample.IsTic
            : !_lastIsTic;
        _lastIsTic = isTic;

        if (_pendingCount == _pending.Length)
        {
            _pendingHead = (_pendingHead + 1) % _pending.Length;
            _pendingCount--;
        }

        int slot = (_pendingHead + _pendingCount) % _pending.Length;
        _pending[slot] = new PendingSegment
        {
            StartSample = startSample,
            ASample = aSample,
            PeakValue = eventUpdate.Event.PeakValue,
            IsTic = isTic,
        };
        _pendingCount++;
    }

    private void AttachCEvent(TgEvent cEvent)
    {
        double cPeakSample = cEvent.SampleIndex + cEvent.SubSampleOffset;

        // The C belongs to the newest window whose A precedes it (windows
        // overlap, so older windows also contain this C — but it is not their
        // beat's C).
        for (int i = _pendingCount - 1; i >= 0; i--)
        {
            int index = (_pendingHead + i) % _pending.Length;
            ref PendingSegment pending = ref _pending[index];
            if (cPeakSample <= pending.ASample)
            {
                continue;
            }

            if (!pending.HasC)
            {
                pending.HasC = true;
                pending.CPeakSample = cPeakSample;
                pending.COnsetValid = cEvent.OnsetValid;
                pending.COnsetSample = cEvent.OnsetSampleIndex + cEvent.OnsetSubSampleOffset;
            }

            break;
        }
    }

    /// <summary>
    /// Feeds the averager the first <see cref="BeatNoiseAverager.LaneWindowMs"/>
    /// of every pending window whose envelope data has arrived (well before the
    /// full window completes, so Scope 2 progress leads Scope 1 strips).
    /// </summary>
    private void AccumulateReadyLanes()
    {
        ulong laneSamples = (ulong)Math.Ceiling(BeatNoiseAverager.LaneWindowMs / 1000.0 * _sampleRate);
        for (int i = 0; i < _pendingCount; i++)
        {
            int index = (_pendingHead + i) % _pending.Length;
            ref PendingSegment pending = ref _pending[index];
            if (pending.LaneAccumulated)
            {
                continue;
            }

            if (_envelopeEndSample < pending.StartSample + laneSamples)
            {
                // Pending windows open in stream order, so none after this one
                // can be ready either.
                break;
            }

            DecimateWindow(pending.StartSample, BeatNoiseAverager.LaneWindowMs, _laneScratch);
            if (_averager.Add(pending.IsTic, _laneScratch))
            {
                _dirty = true;
            }

            pending.LaneAccumulated = true;
        }
    }

    private void CompleteReadySegments()
    {
        ulong windowSamples = (ulong)Math.Ceiling(WindowMs / 1000.0 * _sampleRate);
        while (_pendingCount > 0)
        {
            ref PendingSegment oldest = ref _pending[_pendingHead];
            if (_envelopeEndSample < oldest.StartSample + windowSamples)
            {
                // Pending windows open in stream order, so none after this one
                // can be complete either.
                break;
            }

            // Raw is published only when the whole window was actually written
            // to the raw ring and is still resident. The steady loop feeds raw
            // ahead of the envelope, so this holds; it fails only for a window
            // pushed to completion by the silent end-of-stream flush (which
            // advances the envelope without a matching AppendRaw) — that window
            // falls back to the envelope instead of reading stale ring memory.
            bool rawCovered = IsRawWindowResident(oldest.StartSample, windowSamples);
            CompleteSegment(in oldest, rawCovered);
            _pendingHead = (_pendingHead + 1) % _pending.Length;
            _pendingCount--;
        }
    }

    /// <summary>
    /// True when <see cref="AppendRaw"/> has written the full window range
    /// [start, start + windowSamples) and none of it has yet been overwritten by
    /// a ring wrap, so <see cref="DecimateWindowMinMax"/> reads only real raw.
    /// </summary>
    private bool IsRawWindowResident(ulong startSample, ulong windowSamples)
    {
        if (!_rawFed || startSample + windowSamples > _rawEndSample)
        {
            return false;
        }

        ulong oldestResident = _rawEndSample > (ulong)_rawRing.Length
            ? _rawEndSample - (ulong)_rawRing.Length
            : 0UL;
        return startSample >= oldestResident;
    }

    private void CompleteSegment(in PendingSegment pending, bool rawCovered)
    {
        float[] buffer = AcquireSegmentBuffer(out int poolIndex);
        DecimateWindow(pending.StartSample, WindowMs, buffer);

        // The raw window decimates from the parallel raw ring at the same window
        // start, so its min/max points align with the envelope points 1:1.
        if (rawCovered)
        {
            DecimateWindowMinMax(pending.StartSample, WindowMs, _rawMinPool[poolIndex], _rawMaxPool[poolIndex]);
        }

        double samplesToMs = 1000.0 / _sampleRate;
        double cPeakOffsetMs = pending.HasC
            ? (pending.CPeakSample - pending.StartSample) * samplesToMs
            : 0.0;
        double cOnsetOffsetMs = pending.HasC && pending.COnsetValid
            ? (pending.COnsetSample - pending.StartSample) * samplesToMs
            : 0.0;

        int slot;
        if (_completedCount == SegmentRingCount)
        {
            slot = _completedHead;
            _completedHead = (_completedHead + 1) % SegmentRingCount;
        }
        else
        {
            slot = (_completedHead + _completedCount) % SegmentRingCount;
            _completedCount++;
        }

        _completed[slot] = new CompletedSegment
        {
            Buffer = buffer,
            PoolIndex = poolIndex,
            RawValid = rawCovered,
            StartTimeS = pending.StartSample / (double)_sampleRate,
            IsTic = pending.IsTic,
            AOffsetMs = (pending.ASample - pending.StartSample) * samplesToMs,
            PeakValue = pending.PeakValue,
            CPeakValid = pending.HasC && cPeakOffsetMs < WindowMs,
            CPeakOffsetMs = cPeakOffsetMs,
            COnsetValid = pending.HasC && pending.COnsetValid && cOnsetOffsetMs is >= 0.0 and < WindowMs,
            COnsetOffsetMs = cOnsetOffsetMs,
        };
        _dirty = true;
    }

    /// <summary>
    /// Next reusable pool buffer, skipping every protected one (in the
    /// completed ring or published by one of the two most recent snapshots).
    /// The protected set is at most 24 of the 28 buffers (see the class doc),
    /// so the scan always succeeds; the trailing fallback is unreachable and
    /// only keeps the method total.
    /// </summary>
    private float[] AcquireSegmentBuffer(out int poolIndex)
    {
        for (int probe = 0; probe < SegmentPoolCount; probe++)
        {
            int candidate = (_nextPoolBuffer + probe) % SegmentPoolCount;
            if (IsBufferProtected(candidate))
            {
                continue;
            }

            _nextPoolBuffer = (candidate + 1) % SegmentPoolCount;
            poolIndex = candidate;
            return _segmentPool[candidate];
        }

        poolIndex = _nextPoolBuffer;
        _nextPoolBuffer = (_nextPoolBuffer + 1) % SegmentPoolCount;
        return _segmentPool[poolIndex];
    }

    private bool IsBufferProtected(int poolIndex)
    {
        ulong published = _bufferPublishedVersion[poolIndex];
        if (published != 0 && _version - published <= 1)
        {
            return true;
        }

        for (int i = 0; i < _completedCount; i++)
        {
            if (_completed[(_completedHead + i) % SegmentRingCount].PoolIndex == poolIndex)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fills <paramref name="target"/> with the envelope maximum of each
    /// equal-width bucket across the window (envelope-preserving decimation,
    /// the SweepFrameProjector bin policy).
    /// </summary>
    private void DecimateWindow(ulong startSample, double windowMs, float[] target)
    {
        int ringLength = _envelopeRing.Length;
        double samplesPerPoint = windowMs / 1000.0 * _sampleRate / target.Length;
        for (int point = 0; point < target.Length; point++)
        {
            ulong from = startSample + (ulong)(point * samplesPerPoint);
            ulong to = startSample + (ulong)((point + 1) * samplesPerPoint);
            if (to <= from)
            {
                to = from + 1;
            }

            float max = _envelopeRing[(int)(from % (ulong)ringLength)];
            for (ulong sample = from + 1; sample < to; sample++)
            {
                float value = _envelopeRing[(int)(sample % (ulong)ringLength)];
                if (value > max)
                {
                    max = value;
                }
            }

            target[point] = max;
        }
    }

    /// <summary>
    /// Fills <paramref name="minTarget"/> / <paramref name="maxTarget"/> with the
    /// minimum and maximum raw sample of each equal-width bucket across the
    /// window (min/max decimation preserves the bipolar extent of the
    /// un-rectified signal, unlike the envelope's max-only decimation). The two
    /// targets are the same length and share the envelope's point grid.
    /// </summary>
    private void DecimateWindowMinMax(ulong startSample, double windowMs, float[] minTarget, float[] maxTarget)
    {
        int ringLength = _rawRing.Length;
        double samplesPerPoint = windowMs / 1000.0 * _sampleRate / maxTarget.Length;
        for (int point = 0; point < maxTarget.Length; point++)
        {
            ulong from = startSample + (ulong)(point * samplesPerPoint);
            ulong to = startSample + (ulong)((point + 1) * samplesPerPoint);
            if (to <= from)
            {
                to = from + 1;
            }

            float min = _rawRing[(int)(from % (ulong)ringLength)];
            float max = min;
            for (ulong sample = from + 1; sample < to; sample++)
            {
                float value = _rawRing[(int)(sample % (ulong)ringLength)];
                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }

            minTarget[point] = min;
            maxTarget[point] = max;
        }
    }
}
