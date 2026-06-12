// In-memory adverse-condition scenario rows for the headless verifier.
// Each row streams a deterministic synthetic watch (weak signal, heavy noise,
// impulse storms, gain steps) straight into the shared detector/metrics engine
// (no WAV round-trip; the RIFF parsing path stays covered by the legacy
// fixtures) and scores the detected A events against the FillF32 ground-truth
// side channel. Baseline rows either PIN a known weakness (FAIL when the
// weakness disappears unannounced) or report INFO-only quality numbers.

using System.Globalization;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Core.Sim;

namespace TimeGrapher.Verify;

/// <summary>
/// One verification arm: detector options + optional PLL veto gate.
/// "baseline" and "robust" carry per-row expectations; ad-hoc opts arms
/// ("floor", "guard", "gate", "floor+gate", ...) report INFO only and exist
/// for individual attribution measurements.
/// </summary>
internal sealed record ArmSpec(string Name, TgDetectorOptions? Options, bool UsePllGate)
{
    internal static ArmSpec Baseline { get; } = new("baseline", null, false);

    internal static ArmSpec Robust { get; } = new(
        "robust", TgDetectorOptions.Robust(), UsePllGate: true);

    /// <summary>Parses an arm spec; null on unknown token (usage error).</summary>
    internal static ArmSpec? Parse(string spec)
    {
        if (spec == "baseline")
        {
            return Baseline;
        }
        if (spec == "robust")
        {
            return Robust;
        }

        bool floor = false, guard = false, gate = false;
        foreach (string token in spec.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "floor": floor = true; break;
                case "guard": guard = true; break;
                case "gate": gate = true; break;
                default: return null;
            }
        }
        if (!floor && !guard && !gate)
        {
            return null;
        }
        TgDetectorOptions? options = (floor || guard)
            ? new TgDetectorOptions { EnableAdaptiveFloor = floor, EnableRegimeGuard = guard }
            : null;
        return new ArmSpec(spec, options, gate);
    }
}

/// <summary>
/// Gate set for one scenario arm. Unset members are not gated. Max* bounds on
/// badness (MaxRecall, MinResets) PIN a known weakness: they fail when the
/// weakness disappears unannounced, keeping the evidence chain honest.
/// </summary>
internal sealed record AdverseGates(
    bool? MustSync = null,
    double MinRecall = double.NaN,
    double MaxRecall = double.NaN,
    double MinPrecision = double.NaN,
    int MinResets = -1,
    int MaxResets = -1,
    bool InfoOnly = false);

internal sealed record AdverseScenario(
    string Name,
    int Bph,
    int SampleRate,
    int Seconds,
    double PcmPeak,
    double NoisePeak,
    bool Realistic,
    double ImpulseRate = 0.0,
    double ImpulseAmp = 0.0,
    int SilenceLeadInSamples = 0,
    double GainStepAtS = 0.0,
    double GainStepFactor = 1.0,
    double EvalStartS = 2.0,
    AdverseGates? Baseline = null,
    AdverseGates? Robust = null);

internal static class AdverseScenarios
{
    private const int BlockSize = 4096;

    /// <summary>
    /// Scenario table. Gate values are calibrated against the fixed-seed
    /// streams (see the commit body for the measured numbers); rows whose
    /// behavior sits on a knife edge stay INFO-only.
    /// </summary>
    internal static readonly AdverseScenario[] Rows =
    {
        // Weak signal (W-1/W-2 territory). Robust measured: recall unchanged
        // 0.542 but precision 0.542 -> 1.000 (the PLL veto drops every
        // mistimed event); gated with margin.
        new("weak-1", Bph: 21600, SampleRate: 48000, Seconds: 14,
            PcmPeak: 0.06, NoisePeak: 0.008, Realistic: true,
            Baseline: new AdverseGates(InfoOnly: true),
            Robust: new AdverseGates(MustSync: true, MinPrecision: 0.90)),
        // Measured baseline: Synced but recall 0.043 - the W-2 SNR floor
        // rejects nearly every weak tick. Pinned well above the measurement.
        // Robust precision rises 0.043 -> 0.750 but on single-digit matched
        // counts, too fragile to gate: INFO.
        new("weak-2", Bph: 18000, SampleRate: 48000, Seconds: 16,
            PcmPeak: 0.035, NoisePeak: 0.010, Realistic: false,
            Baseline: new AdverseGates(MaxRecall: 0.30)),
        // Sustained broadband noise (W-7). KNOWN ROBUST REGRESSION: the PLL
        // phase here is itself dragged by noise (W-8 latch), so its match
        // verdict anti-correlates with truth and the gate vetoes true beats
        // (recall 0.167 -> 0.042, attribution: gate arm alone reproduces it,
        // floor/guard arms are neutral). Exactly the regime where a future
        // shape-based gate that does not depend on the corrupted phase
        // estimate should win; kept INFO and recorded honestly.
        new("noisy-1", Bph: 21600, SampleRate: 48000, Seconds: 14,
            PcmPeak: 0.25, NoisePeak: 0.08, Realistic: true,
            Baseline: new AdverseGates(InfoOnly: true)),
        new("noisy-2", Bph: 28800, SampleRate: 48000, Seconds: 14,
            PcmPeak: 0.20, NoisePeak: 0.12, Realistic: false,
            Baseline: new AdverseGates(InfoOnly: true)),
        // Impulse storms (W-3 regime-reset DoS, W-5/W-8 contamination).
        // Measured baseline: 7 detector resets in 16 s, lock never survives -
        // the W-3 regime-reset DoS. Pinned at >= 3 resets. Robust measured:
        // 0 resets, Synced 21600, recall 0.083 -> 0.262.
        // Mirrored in-process by tests/TimeGrapher.Core.Tests/
        // DetectorStressScenarioTests.cs - keep parameters and gates in sync
        // when recalibrating.
        new("impulse-dos", Bph: 21600, SampleRate: 48000, Seconds: 16,
            PcmPeak: 0.03, NoisePeak: 0.004, Realistic: false,
            ImpulseRate: 1.0, ImpulseAmp: 0.95,
            Baseline: new AdverseGates(MinResets: 3),
            Robust: new AdverseGates(MustSync: true, MaxResets: 1, MinRecall: 0.15)),
        // Robust measured: precision 0.911 -> 0.981 at 0.9pp recall cost.
        new("impulse-storm", Bph: 28800, SampleRate: 48000, Seconds: 16,
            PcmPeak: 0.25, NoisePeak: 0.02, Realistic: false,
            ImpulseRate: 3.0, ImpulseAmp: 0.6,
            Baseline: new AdverseGates(InfoOnly: true),
            Robust: new AdverseGates(MinRecall: 0.85, MinPrecision: 0.95)),
        // Loud-to-quiet gain step (W-4(b) latch-up).
        // Measured baseline: sync lost at the 6 s gain step and never
        // re-acquired (W-4(b): reference peak latched high, no decay path).
        // Robust measured: re-locks Synced 21600, post-step recall 0.694.
        // Mirrored in-process by tests/TimeGrapher.Core.Tests/
        // DetectorStressScenarioTests.cs - keep parameters and gates in sync
        // when recalibrating.
        new("quiet-step", Bph: 21600, SampleRate: 48000, Seconds: 16,
            PcmPeak: 0.60, NoisePeak: 0.01, Realistic: false,
            GainStepAtS: 6.0, GainStepFactor: 0.13, EvalStartS: 10.0,
            Baseline: new AdverseGates(MustSync: false),
            Robust: new AdverseGates(MustSync: true, MinRecall: 0.50)),
        // Bootstrap behind a silent lead-in (W-2/W-13 bootstrap paths).
        // Measured baseline: 12 detector resets and NO lock within 12 s of a
        // healthy 0.30-peak watch - the silence-collapsed noise floor makes
        // every early tick trip the regime detector, each trip wiping the BPH
        // history before the 1.5 s auto-detect window can complete. Robust is
        // measured UNCHANGED (the storm is the TG_REGIME_FLOOR-straddling
        // absFloorJump cycle, W-4(c), which run persistence cannot break):
        // remains a pinned known weakness.
        new("leadin-quiet", Bph: 21600, SampleRate: 48000, Seconds: 12,
            PcmPeak: 0.30, NoisePeak: 0.01, Realistic: false,
            SilenceLeadInSamples: 96000,
            Baseline: new AdverseGates(MustSync: false)),
        // No watch at all: the detector must NOT lock onto noise (false-lock
        // guard). Gated in BOTH arms so no option can ever buy detection
        // power at the price of false locks.
        new("noise-only", Bph: 21600, SampleRate: 48000, Seconds: 12,
            PcmPeak: 0.0, NoisePeak: 0.05, Realistic: false,
            Baseline: new AdverseGates(MustSync: false),
            Robust: new AdverseGates(MustSync: false)),
    };

    internal sealed record RowResult(
        string Name,
        string Profile,
        TgSyncStatus SyncStatus,
        int DetectedBph,
        DetectionScorer.Score Score,
        ulong MissedBeats,
        uint SyncLossCount,
        int Resets,
        ulong Vetoed,
        string Verdict);

    /// <summary>Runs every row under one arm; false when any gated row fails.</summary>
    internal static bool Run(TextWriter output, ArmSpec arm)
    {
        bool allOk = true;
        foreach (AdverseScenario row in Rows)
        {
            RowResult result = RunRow(row, arm);
            allOk &= result.Verdict != "FAIL";
            output.WriteLine(Format(result));
        }
        return allOk;
    }

    /// <summary>
    /// Runs every row under both arms (two-way gating: arm A pins its
    /// expectations, arm B proves its own) and prints paired deltas.
    /// </summary>
    internal static bool RunAb(TextWriter output, ArmSpec armA, ArmSpec armB)
    {
        bool allOk = true;
        foreach (AdverseScenario row in Rows)
        {
            RowResult a = RunRow(row, armA);
            RowResult b = RunRow(row, armB);
            allOk &= a.Verdict != "FAIL" && b.Verdict != "FAIL";
            output.WriteLine(Format(a));
            output.WriteLine(Format(b));
            string verdict = a.Verdict == "FAIL" || b.Verdict == "FAIL" ? "FAIL" : "PASS";
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "AB {0}: recall {1:F3}->{2:F3} ({3:+0.000;-0.000;+0.000}) | precision {4:F3}->{5:F3} ({6:+0.000;-0.000;+0.000}) | resets {7}->{8} | sync {9}->{10} | verdict={11}",
                row.Name,
                a.Score.Recall, b.Score.Recall, b.Score.Recall - a.Score.Recall,
                a.Score.Precision, b.Score.Precision, b.Score.Precision - a.Score.Precision,
                a.Resets, b.Resets, a.SyncStatus, b.SyncStatus, verdict));
        }
        return allOk;
    }

    internal static RowResult RunRow(AdverseScenario row, ArmSpec arm)
    {
        WatchSynthStreamConfig synthConfig = row.Realistic
            ? WatchSynthStreamConfig.Realistic()
            : WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = (uint)row.SampleRate;
        synthConfig.Bph = row.Bph;
        synthConfig.PcmPeakAmplitude = row.PcmPeak;
        synthConfig.NoisePeakAmplitude = row.NoisePeak;
        if (row.ImpulseRate > 0.0)
        {
            synthConfig.ImpulseNoiseRatePerSecond = row.ImpulseRate;
            synthConfig.ImpulseNoisePeakAmplitude = row.ImpulseAmp;
        }

        var synth = new WatchSynthStream(synthConfig);
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: row.SampleRate,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0,
            DetectorOptions: arm.Options,
            EventGate: arm.UsePllGate ? new BeatEventGateConfig(new PllMatchGate()) : null));

        double leadInS = (double)row.SilenceLeadInSamples / row.SampleRate;
        var truthTimes = new List<double>();
        var detectedATimes = new List<double>();
        int resets = 0;

        var block = new float[BlockSize];
        var eventBuf = new WatchSynthStreamEvent[64];
        DetectorMetricsBlockUpdate update;

        int silenceRemaining = row.SilenceLeadInSamples;
        while (silenceRemaining > 0)
        {
            int slice = Math.Min(block.Length, silenceRemaining);
            Array.Clear(block, 0, slice);
            update = engine.Process(block.AsSpan(0, slice));
            Collect(update, row.SampleRate, detectedATimes, ref resets);
            silenceRemaining -= slice;
        }

        long generated = 0;
        long totalSamples = (long)row.SampleRate * row.Seconds;
        long gainStepSample = row.GainStepAtS > 0.0
            ? (long)(row.GainStepAtS * row.SampleRate)
            : long.MaxValue;
        while (generated < totalSamples)
        {
            int slice = (int)Math.Min(block.Length, totalSamples - generated);
            Span<float> span = block.AsSpan(0, slice);
            WatchSynthStreamFillResult fill = synth.FillF32(span, eventBuf);
            for (int i = 0; i < fill.EventsWritten; i++)
            {
                truthTimes.Add(leadInS + eventBuf[i].TimeS);
            }
            for (int i = 0; i < slice; i++)
            {
                if (generated + i >= gainStepSample)
                {
                    span[i] *= (float)row.GainStepFactor;
                }
            }
            update = engine.Process(span);
            Collect(update, row.SampleRate, detectedATimes, ref resets);
            generated += slice;
        }

        update = engine.Flush();
        Collect(update, row.SampleRate, detectedATimes, ref resets);

        DetectionScorer.Score score = DetectionScorer.Match(
            truthTimes.ToArray(), detectedATimes.ToArray(),
            toleranceS: 0.005, evalStartS: leadInS + row.EvalStartS);

        DetectorResultSnapshot snapshot = update.Result;
        AdverseGates gates = arm.Name switch
        {
            "baseline" => row.Baseline ?? new AdverseGates(InfoOnly: true),
            "robust" => row.Robust ?? new AdverseGates(InfoOnly: true),
            _ => new AdverseGates(InfoOnly: true), // attribution arms report only
        };
        string verdict = Evaluate(gates, snapshot, score, resets);

        return new RowResult(
            row.Name, arm.Name, snapshot.SyncStatus, snapshot.DetectedBph, score,
            snapshot.MissedBeats, snapshot.SyncLossCount, resets,
            snapshot.VetoedEvents, verdict);
    }

    private static void Collect(
        DetectorMetricsBlockUpdate update, int sampleRate,
        List<double> detectedATimes, ref int resets)
    {
        if (update.Result.DetectorResetEvent)
        {
            resets++;
        }
        foreach (DetectedEventUpdate ev in update.Events)
        {
            if (ev.Event.Type == TgEventType.A)
            {
                detectedATimes.Add(ev.EventSample / sampleRate);
            }
        }
    }

    private static string Evaluate(
        AdverseGates gates, DetectorResultSnapshot snapshot,
        DetectionScorer.Score score, int resets)
    {
        if (gates.InfoOnly)
        {
            return "INFO";
        }

        bool ok = true;
        if (gates.MustSync.HasValue)
        {
            ok &= gates.MustSync.Value == (snapshot.SyncStatus == TgSyncStatus.Synced);
        }
        if (!double.IsNaN(gates.MinRecall))
        {
            ok &= score.Recall >= gates.MinRecall;
        }
        if (!double.IsNaN(gates.MaxRecall))
        {
            ok &= score.Recall <= gates.MaxRecall;
        }
        if (!double.IsNaN(gates.MinPrecision))
        {
            ok &= score.Precision >= gates.MinPrecision;
        }
        if (gates.MinResets >= 0)
        {
            ok &= resets >= gates.MinResets;
        }
        if (gates.MaxResets >= 0)
        {
            ok &= resets <= gates.MaxResets;
        }
        return ok ? "PASS" : "FAIL";
    }

    private static string Format(RowResult r)
    {
        return string.Format(CultureInfo.InvariantCulture,
            "ADV {0}: profile={1} sync={2} bph={3} recall={4:F3} precision={5:F3} " +
            "a_bias_ms={6:F3} a_rms_ms={7:F3} missed={8} sync_losses={9} resets={10} vetoed={11} verdict={12}",
            r.Name, r.Profile, r.SyncStatus, r.DetectedBph, r.Score.Recall, r.Score.Precision,
            r.Score.MedianOffsetMs, r.Score.RmsAfterOffsetMs,
            r.MissedBeats, r.SyncLossCount, r.Resets, r.Vetoed, r.Verdict);
    }
}
