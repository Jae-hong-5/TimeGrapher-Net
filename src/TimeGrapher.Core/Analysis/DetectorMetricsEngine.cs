using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis;

public sealed record DetectorMetricsEngineConfig(
    int SampleRate,
    double LiftAngle,
    int AveragingPeriod,
    bool UseCOnset,
    bool AutoBph,
    int ManualBph,
    double HpfCutoffHz,
    BeatEventGateConfig? EventGate = null,
    // Seconds added to the measured A->C interval before amplitude is computed, to
    // offset the envelope front-end's A-onset detection latency (A is timestamped at
    // a threshold crossing that lags the true onset more than the C peak does). The
    // default ~45 us is the A-onset-minus-C-peak latency characterised against the
    // reference synthesiser at the default 0.15 ms envelope smoothing; it is exposed
    // here so a real measuring rig can recalibrate it. 0 disables compensation.
    double AmplitudeOnsetLatencyS = 0.000045,
    BeatLandmarkRefinerConfig? Refiner = null,
    double PhaseGuideOnsetRescueScale = 0.0);

public readonly record struct DetectedEventUpdate(
    TgEvent Event,
    double EventSample,
    WatchMetricsUpdate MetricsUpdate);

public sealed record DetectorMetricsBlockUpdate(
    DetectorResultSnapshot Result,
    IReadOnlyList<DetectedEventUpdate> DisplayEvents,
    IReadOnlyList<DetectedEventUpdate> MetricsEvents)
{
    public DetectorMetricsBlockUpdate(
        DetectorResultSnapshot result,
        IReadOnlyList<DetectedEventUpdate> events)
        : this(result, events, events)
    {
    }
}

public sealed record DetectorResultSnapshot(
    TgSyncStatus SyncStatus,
    int DetectedBph,
    double MeasuredPeriodS,
    IReadOnlyList<TgEvent> Events,
    ReadOnlyMemory<float> ProcessedPcm,
    int ProcessedPcmLen,
    ulong ProcessedPcmStartSample,
    bool SyncLostEvent,
    bool SyncAcquiredEvent,
    bool DetectorResetEvent,
    float OnsetThreshold,
    float MinPeakThreshold,
    float NoiseFloor,
    float ReferencePeak,
    ulong MissedBeats = 0,
    uint SyncLossCount = 0,
    ulong VetoedEvents = 0);

/// <summary>
/// Shared detector + metrics pipeline used by the live worker and the headless
/// verifier so their event/metric contracts cannot drift apart.
///
/// Gate semantics: when an event gate is configured, DisplayEvents and
/// MetricsEvents carry the same POST-gate stream. The raw detector stream
/// remains observable through Result.Events and Result.VetoedEvents.
/// </summary>
public sealed class DetectorMetricsEngine
{
    private readonly DetectorMetricsEngineConfig _config;
    private readonly WatchMetrics _metrics;
    private readonly TgDetector _detector;
    private readonly BeatEventGateHost? _gate;
    private readonly BeatLandmarkRefinerHost? _refiner;
    private readonly TgResult _result = new();
    private uint _syncLossCount;

    public DetectorMetricsEngine(DetectorMetricsEngineConfig config)
    {
        _config = config;
        _metrics = new WatchMetrics(new WatchMetricsConfig
        {
            SampleRate = config.SampleRate,
            LiftAngle = config.LiftAngle,
            AveragingPeriod = config.AveragingPeriod,
            MaxRateDataPoints = 250,
            RateErrorYScale = 10.0,
            RlsWindowInit = 100,
            AmplitudeOnsetLatencyS = config.AmplitudeOnsetLatencyS,
        });

        TgConfig detectorConfig = TgConfig.Default();
        detectorConfig.SampleRate = config.SampleRate;
        detectorConfig.BphMode = config.AutoBph ? TgBphMode.Auto : TgBphMode.Manual;
        detectorConfig.ManualBph = config.ManualBph;
        detectorConfig.SuppressPreSyncEvents = true;
        detectorConfig.HpfCutoffHz = config.HpfCutoffHz;
        detectorConfig.PhaseGuideOnsetRescueScale = config.PhaseGuideOnsetRescueScale;

        /* The gate consumes the per-event PLL match verdicts, so configuring
         * a gate implies TrackEventPllMatch on the detector. */
        if (config.EventGate != null)
        {
            detectorConfig.TrackEventPllMatch = true;
            _gate = new BeatEventGateHost(config.EventGate.Gate, config.SampleRate);
        }

        /* The landmark refiner re-times the metrics/display stream only; it needs
         * no per-event PLL verdict (it reasons over the A/C envelope window), so
         * unlike the gate it does not force TrackEventPllMatch. */
        if (config.Refiner != null)
        {
            _refiner = new BeatLandmarkRefinerHost(config.Refiner, config.SampleRate);
        }

        _detector = new TgDetector(detectorConfig);
        _metrics.Reset();
    }

    public DetectorMetricsBlockUpdate Process(ReadOnlySpan<float> block)
    {
        _detector.Process(block, _result);
        return BuildUpdate(endOfStream: false);
    }

    public DetectorMetricsBlockUpdate Flush()
    {
        _detector.Flush(_result);
        return BuildUpdate(endOfStream: true);
    }

    private DetectorMetricsBlockUpdate BuildUpdate(bool endOfStream)
    {
        bool synced = _result.SyncStatus == TgSyncStatus.Synced;
        if (_result.SyncLostEvent)
        {
            _syncLossCount++;
        }
        var displayUpdates = new List<DetectedEventUpdate>(_result.Events.Count);
        var metricsUpdates = new List<DetectedEventUpdate>(_result.Events.Count);

        if (_refiner != null)
        {
            _refiner.AppendEnvelope(_result.ProcessedPcm, _result.ProcessedPcmLen, _result.ProcessedPcmStartSample);
            foreach (TgEvent ev in _result.Events)
            {
                _refiner.Submit(ev, synced, _result.DetectedBph, _result.MeasuredPeriodS,
                                _result.NoiseFloor, _result.ReferencePeak);
            }

            /* Force-release pending beats at stream and sync boundaries so the
             * refiner never holds an event across a state flush. */
            bool force = endOfStream || _result.SyncLostEvent || _result.DetectorResetEvent;
            foreach (BeatLandmarkRefinerHost.RefinedEvent refined in _refiner.Release(force))
            {
                double eventSample = EventSample(refined.Event);
                WatchMetricsUpdate metricsUpdate = refined.Event.Type switch
                {
                    TgEventType.A => _metrics.HandleAEvent(eventSample, refined.Synced, refined.DetectedBph),
                    TgEventType.C => _metrics.HandleCEvent(eventSample, refined.Synced, refined.DetectedBph),
                    _ => new WatchMetricsUpdate(),
                };
                var update = new DetectedEventUpdate(refined.Event, eventSample, metricsUpdate);
                displayUpdates.Add(update);
                metricsUpdates.Add(update);
            }
            if (_result.SyncLostEvent || _result.DetectorResetEvent)
            {
                _refiner.ResetRefiner();
            }
        }
        else if (_gate == null)
        {
            foreach (TgEvent ev in _result.Events)
            {
                double eventSample = EventSample(ev);
                WatchMetricsUpdate metricsUpdate = ev.Type switch
                {
                    TgEventType.A => _metrics.HandleAEvent(eventSample, synced, _result.DetectedBph),
                    TgEventType.C => _metrics.HandleCEvent(eventSample, synced, _result.DetectedBph),
                    _ => new WatchMetricsUpdate(),
                };

                var update = new DetectedEventUpdate(ev, eventSample, metricsUpdate);
                displayUpdates.Add(update);
                metricsUpdates.Add(update);
            }
        }
        else
        {
            _gate.AppendEnvelope(_result.ProcessedPcm, _result.ProcessedPcmLen, _result.ProcessedPcmStartSample);

            ReadOnlySpan<byte> pllMatch = _detector.LastEventPllMatch;
            for (int i = 0; i < _result.Events.Count; i++)
            {
                TgEvent ev = _result.Events[i];
                double eventSample = EventSample(ev);

                bool matched = i >= pllMatch.Length || pllMatch[i] != 0;
                var candidate = new BeatCandidate(
                    ev, synced, _result.DetectedBph, _result.MeasuredPeriodS,
                    _result.NoiseFloor, _result.ReferencePeak, matched);
                _gate.Submit(ev, eventSample, candidate);
            }

            /* Force-release pending events at stream and sync boundaries so
             * the gate never swallows events across a state flush. */
            bool force = endOfStream || _result.SyncLostEvent || _result.DetectorResetEvent;
            foreach (BeatEventGateHost.ReleasedEvent released in _gate.Release(force))
            {
                if (!released.Accepted)
                {
                    continue;
                }
                WatchMetricsUpdate metricsUpdate = released.Event.Type switch
                {
                    TgEventType.A => _metrics.HandleAEvent(
                        released.EventSample, released.Candidate.Synced, released.Candidate.DetectedBph),
                    TgEventType.C => _metrics.HandleCEvent(
                        released.EventSample, released.Candidate.Synced, released.Candidate.DetectedBph),
                    _ => new WatchMetricsUpdate(),
                };
                var update = new DetectedEventUpdate(released.Event, released.EventSample, metricsUpdate);
                displayUpdates.Add(update);
                metricsUpdates.Add(update);
            }
            if (_result.SyncLostEvent || _result.DetectorResetEvent)
            {
                _gate.ResetGate();
            }
        }

        TgEvent[] eventsSnapshot = _result.Events.ToArray();
        var processedPcmSnapshot = new float[_result.ProcessedPcmLen];
        if (_result.ProcessedPcmLen > 0)
        {
            Array.Copy(_result.ProcessedPcm, processedPcmSnapshot, _result.ProcessedPcmLen);
        }

        var resultSnapshot = new DetectorResultSnapshot(
            _result.SyncStatus,
            _result.DetectedBph,
            _result.MeasuredPeriodS,
            eventsSnapshot,
            processedPcmSnapshot,
            _result.ProcessedPcmLen,
            _result.ProcessedPcmStartSample,
            _result.SyncLostEvent,
            _result.SyncAcquiredEvent,
            _result.DetectorResetEvent,
            _result.OnsetThreshold,
            _result.MinPeakThreshold,
            _result.NoiseFloor,
            _result.ReferencePeak,
            _metrics.MissedBeats,
            _syncLossCount,
            _gate?.VetoedEvents ?? 0);

        return new DetectorMetricsBlockUpdate(resultSnapshot, displayUpdates, metricsUpdates);
    }

    private double EventSample(TgEvent ev)
    {
        if (ev.Type == TgEventType.C && _config.UseCOnset && ev.OnsetValid)
        {
            return ev.OnsetSampleIndex + ev.OnsetSubSampleOffset;
        }

        return ev.SampleIndex + ev.SubSampleOffset;
    }
}
