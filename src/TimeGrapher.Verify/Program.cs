// Headless verification harness: analyses sample WAV files through the ported
// detection/metrics pipeline (no threads) and checks the detected BPH against the
// filename. Mirrors TAnalysisWorker's per-event handling, run synchronously.
//
// Usage:
//   TimeGrapher.Verify <wav-or-dir> [<wav-or-dir> ...]
// A directory argument is expanded to its *.wav files.
//   TimeGrapher.Verify --generated --byte-fixtures
// Adds deterministic generated and byte-built WAV fixtures for CI.
//   TimeGrapher.Verify --adverse [--gate=off|pll]
// Adverse-condition rows with optional PLL event-gate measurement.
//   TimeGrapher.Verify <wav-or-dir> [--landmark=off|stub:noop|stub:cpeak]
// Re-times the metrics/display A/C landmarks through a refiner (off = unchanged).
//   TimeGrapher.Verify --export-training=<dir>
// Writes weak-A-weighted synthetic refiner-training rows to <dir>/landmark_training.csv.
//   TimeGrapher.Verify <wav-or-dir> --diagnose [--landmark=...]
// Prints the per-file B->A signature (A phase residual + A->C dips); add --landmark to compare arms.
//
// Exit codes: 0 = all gates passed, 1 = a verification gate failed,
// 2 = usage error (unknown option, malformed spec, flags without a runner).

using System.Globalization;
using System.Text.RegularExpressions;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using TimeGrapher.Inference;
using TimeGrapher.Verify;

const int DetectorNumberOfSamples = 4096;

// Collect WAV paths: a directory argument expands to its *.wav files.
var files = new List<string>();
var generatedFiles = new List<string>();
// Ground-truth beat times (seconds, file-relative) for generated fixtures,
// captured from the synth's FillF32 event side channel at write time.
var expectationsByFile = new Dictionary<string, GeneratedFixtureExpectation>(StringComparer.OrdinalIgnoreCase);
bool runAdverse = false;
string gateSpec = "off";
string landmarkSpec = "off";
string? exportTrainingDir = null;
bool diagnose = false;
double rescueScale = 0.0;
foreach (string arg in args)
{
    if (arg == "--adverse")
    {
        runAdverse = true;
        continue;
    }

    if (arg.StartsWith("--gate=", StringComparison.Ordinal))
    {
        gateSpec = arg["--gate=".Length..];
        continue;
    }

    if (arg.StartsWith("--landmark=", StringComparison.Ordinal))
    {
        landmarkSpec = arg["--landmark=".Length..];
        continue;
    }

    if (arg.StartsWith("--export-training=", StringComparison.Ordinal))
    {
        exportTrainingDir = arg["--export-training=".Length..];
        continue;
    }

    if (arg == "--diagnose")
    {
        diagnose = true;
        continue;
    }

    if (arg.StartsWith("--rescue=", StringComparison.Ordinal))
    {
        rescueScale = double.Parse(arg["--rescue=".Length..], CultureInfo.InvariantCulture);
        continue;
    }

    if (arg == "--generated")
    {
        generatedFiles.AddRange(GenerateSyntheticFixtures(expectationsByFile));
        continue;
    }

    if (arg == "--byte-fixtures")
    {
        generatedFiles.AddRange(GenerateByteBuiltFixtures(expectationsByFile));
        continue;
    }

    if (arg.StartsWith("--", StringComparison.Ordinal))
    {
        // An unrecognized option must not fall through to the WAV-path
        // branch (a typo'd flag used to crash WavFileReader with an
        // unhandled exception instead of a usage error).
        Console.Error.WriteLine($"TimeGrapher.Verify: unknown option '{arg}'");
        return 2;
    }

    if (Directory.Exists(arg))
    {
        files.AddRange(Directory.GetFiles(arg, "*.wav").OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }
    else
    {
        files.Add(arg);
    }
}

// Training-data export is a standalone mode (no WAV inputs needed).
if (exportTrainingDir != null)
{
    return TrainingDataExporter.Export(exportTrainingDir);
}

// Gate-selection flags configure only the adverse runner.
if (!runAdverse && gateSpec != "off")
{
    Console.Error.WriteLine("TimeGrapher.Verify: --gate requires --adverse");
    return 2;
}
files.AddRange(generatedFiles);

if (files.Count == 0 && !runAdverse)
{
    // Usage error (exit 2): exit 1 is reserved for verification failures.
    Console.Error.WriteLine("TimeGrapher.Verify: no WAV files specified");
    return 2;
}

bool allMatch = true;

try
{
    if (!TryResolveLandmark(landmarkSpec, out BeatLandmarkRefinerConfig? landmarkRefiner, out string? landmarkError))
    {
        Console.Error.WriteLine("TimeGrapher.Verify: " + landmarkError);
        return 2;
    }
    if (landmarkRefiner != null)
    {
        Console.WriteLine("landmark: " + landmarkRefiner.Refiner.Name);
    }

    if (diagnose)
    {
        // B->A diagnostic over the (optionally refined) metrics stream; run with
        // and without --landmark to compare off vs refiner by the same measure.
        foreach (string file in files)
        {
            BeatDiagnostics.Run(Console.Out, file, landmarkRefiner, rescueScale);
        }
        return 0;
    }

    foreach (string file in files)
    {
        WavData wav = WavFileReader.ReadMonoFloat(file, WavAcceptanceProfile.PlaybackFloatMonoStandardRates);

        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: wav.SampleRate,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0,
            Refiner: landmarkRefiner));

        int detectedBph = 0;
        var syncStatus = TgSyncStatus.NotSynced;
        string resultsText = "";
        var detectedATimes = new List<double>();
        BeatTimingSample lastBeatTiming = default;
        bool haveBeatTiming = false;
        DerivedTimingMeasures lastDerived = default;
        bool haveDerived = false;

        float[] samples = wav.Samples;
        int total = samples.Length;
        int offset = 0;
        while (offset < total)
        {
            int slice = total - offset > DetectorNumberOfSamples
                            ? DetectorNumberOfSamples
                            : total - offset;

            var block = new ReadOnlySpan<float>(samples, offset, slice);
            DetectorMetricsBlockUpdate update = engine.Process(block);
            DetectorResultSnapshot result = update.Result;

            detectedBph = result.DetectedBph;
            syncStatus = result.SyncStatus;

            for (int i = 0; i < update.MetricsEvents.Count; i++)
            {
                if (update.MetricsEvents[i].Event.Type == TgEventType.A)
                {
                    detectedATimes.Add(update.MetricsEvents[i].EventSample / wav.SampleRate);
                }
                if (update.MetricsEvents[i].MetricsUpdate.ResultsUpdated)
                {
                    resultsText = update.MetricsEvents[i].MetricsUpdate.ResultsText;
                }
                if (update.MetricsEvents[i].MetricsUpdate.BeatTimingSampleUpdated)
                {
                    lastBeatTiming = update.MetricsEvents[i].MetricsUpdate.BeatTimingSample;
                    haveBeatTiming = true;
                }
                if (update.MetricsEvents[i].MetricsUpdate.DerivedMeasuresUpdated)
                {
                    lastDerived = update.MetricsEvents[i].MetricsUpdate.DerivedMeasures;
                    haveDerived = true;
                }
            }

            offset += slice;
        }

        // Drain the envelope delay line at end-of-stream.
        DetectorMetricsBlockUpdate flushUpdate = engine.Flush();
        detectedBph = flushUpdate.Result.DetectedBph;
        syncStatus = flushUpdate.Result.SyncStatus;
        for (int i = 0; i < flushUpdate.MetricsEvents.Count; i++)
        {
            if (flushUpdate.MetricsEvents[i].Event.Type == TgEventType.A)
            {
                detectedATimes.Add(flushUpdate.MetricsEvents[i].EventSample / wav.SampleRate);
            }
            if (flushUpdate.MetricsEvents[i].MetricsUpdate.ResultsUpdated)
            {
                resultsText = flushUpdate.MetricsEvents[i].MetricsUpdate.ResultsText;
            }
            if (flushUpdate.MetricsEvents[i].MetricsUpdate.BeatTimingSampleUpdated)
            {
                lastBeatTiming = flushUpdate.MetricsEvents[i].MetricsUpdate.BeatTimingSample;
                haveBeatTiming = true;
            }
            if (flushUpdate.MetricsEvents[i].MetricsUpdate.DerivedMeasuresUpdated)
            {
                lastDerived = flushUpdate.MetricsEvents[i].MetricsUpdate.DerivedMeasures;
                haveDerived = true;
            }
        }

        string name = Path.GetFileName(file);
        // BuildResults wraps live values in ValueSpanStart/End markers that the
        // GUI strips before display; this console report is a display surface too.
        string cleanResults = resultsText
            .Replace(WatchMetrics.ValueSpanStart.ToString(), "")
            .Replace(WatchMetrics.ValueSpanEnd.ToString(), "");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0}: detected_bph={1} sync_status={2} results=[{3}]",
            name, detectedBph, syncStatus, cleanResults));

        if (expectationsByFile.TryGetValue(file, out GeneratedFixtureExpectation? expectation))
        {
            DetectionScorer.Score score = DetectionScorer.Match(
                expectation.TruthTimes, detectedATimes.ToArray(), toleranceS: 0.005, evalStartS: 2.0);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  score: truth={0} detected={1} matched={2} recall={3:F3} precision={4:F3} a_bias_ms={5:F3} a_rms_ms={6:F3}",
                score.TruthCount, score.DetectedCount, score.Matched,
                score.Recall, score.Precision, score.MedianOffsetMs, score.RmsAfterOffsetMs));
            if (score.Recall < 1.0 || score.Precision < 1.0 || score.RmsAfterOffsetMs > 0.5 ||
                Math.Abs(score.MedianOffsetMs) > 0.5)
            {
                allMatch = false;
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  MISMATCH: generated timing score recall={0:F3} precision={1:F3} bias_ms={2:F3} rms_ms={3:F3}",
                    score.Recall, score.Precision, score.MedianOffsetMs, score.RmsAfterOffsetMs));
            }

            if (expectation.ExpectedRateSPerDay is double expectedRate)
            {
                if (!haveBeatTiming || !lastBeatTiming.RateValid ||
                    Math.Abs(lastBeatTiming.RateSPerDay - expectedRate) > expectation.RateToleranceSPerDay)
                {
                    allMatch = false;
                    string actual = haveBeatTiming && lastBeatTiming.RateValid
                        ? lastBeatTiming.RateSPerDay.ToString("F1", CultureInfo.InvariantCulture)
                        : "invalid";
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  MISMATCH: expected rate {0:F1}+/-{1:F1} s/d, detected {2}",
                        expectedRate, expectation.RateToleranceSPerDay, actual));
                }
            }

            if (expectation.ExpectedBeatErrorMs is double expectedBeatError)
            {
                if (!haveBeatTiming || !lastBeatTiming.BeatErrorValid ||
                    Math.Abs(lastBeatTiming.BeatErrorSignedMs - expectedBeatError) > expectation.BeatErrorToleranceMs)
                {
                    allMatch = false;
                    string actual = haveBeatTiming && lastBeatTiming.BeatErrorValid
                        ? lastBeatTiming.BeatErrorSignedMs.ToString("F2", CultureInfo.InvariantCulture)
                        : "invalid";
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  MISMATCH: expected beat_error {0:F2}+/-{1:F2} ms, detected {2}",
                        expectedBeatError, expectation.BeatErrorToleranceMs, actual));
                }
            }

            if (expectation.ExpectedDiffTicTacMs is double expectedDiff)
            {
                if (!haveDerived || !lastDerived.DiffTicTacValid ||
                    Math.Abs(lastDerived.DiffTicTacMs - expectedDiff) > expectation.DiffTicTacToleranceMs)
                {
                    allMatch = false;
                    string actual = haveDerived && lastDerived.DiffTicTacValid
                        ? lastDerived.DiffTicTacMs.ToString("F2", CultureInfo.InvariantCulture)
                        : "invalid";
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  MISMATCH: expected diff_tic_tac {0:F2}+/-{1:F2} ms, detected {2}",
                        expectedDiff, expectation.DiffTicTacToleranceMs, actual));
                }
            }
        }

        if (syncStatus != TgSyncStatus.Synced)
        {
            allMatch = false;
            Console.Error.WriteLine("  MISMATCH: expected sync_status=Synced, detected " + syncStatus);
        }

        if (string.IsNullOrWhiteSpace(resultsText))
        {
            allMatch = false;
            Console.Error.WriteLine("  MISMATCH: no metrics result text was produced");
        }

        // Expected BPH parsed from the filename, e.g. "21600BPH_*.wav" -> 21600.
        Match m = Regex.Match(name, @"(\d+)BPH");
        if (m.Success)
        {
            int expectedBph = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (expectedBph != detectedBph)
            {
                allMatch = false;
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  MISMATCH: expected {0}, detected {1}", expectedBph, detectedBph));
            }
        }
        else
        {
            allMatch = false;
            Console.Error.WriteLine("  no expected BPH in filename: " + name);
        }
    }
}
finally
{
    foreach (string generatedFile in generatedFiles)
    {
        try
        {
            File.Delete(generatedFile);
        }
        catch (IOException)
        {
        }
    }

    // Each generator writes into its own unique temp directory; remove the
    // now-empty directories too (Delete is non-recursive, so a directory that
    // still holds an undeletable file is left behind with it).
    foreach (string? dir in generatedFiles.Select(Path.GetDirectoryName).Distinct())
    {
        if (dir == null)
        {
            continue;
        }

        try
        {
            Directory.Delete(dir);
        }
        catch (IOException)
        {
        }
    }
}

// Adverse-condition scenario rows (in-memory, ground-truth scored).
if (runAdverse)
{
    // Resolve the arm. --gate=onnx:<path> is reserved for the future inference
    // project; using it today (or any unknown value) is a usage error (exit 2).
    if (!AdverseScenarios.TryResolveArm(gateSpec, out ArmSpec arm, out string? gateError))
    {
        Console.Error.WriteLine("TimeGrapher.Verify: " + gateError);
        return 2;
    }

    if (!AdverseScenarios.Run(Console.Out, arm))
    {
        allMatch = false;
    }
}

return allMatch ? 0 : 1;

// Resolves the --landmark spec to a refiner config. off -> null (pipeline
// unchanged); stub:* -> the deterministic stub; onnx:<path> is reserved for the
// future TimeGrapher.Inference refiner. Unknown values are a usage error.
static bool TryResolveLandmark(string spec, out BeatLandmarkRefinerConfig? refiner, out string? error)
{
    refiner = null;
    error = null;
    switch (spec)
    {
        case "off":
            return true;
        case "stub":
        case "stub:cpeak":
            refiner = new BeatLandmarkRefinerConfig(new StubBeatLandmarkRefiner(StubBeatLandmarkRefiner.Mode.CPeak));
            return true;
        case "stub:noop":
            refiner = new BeatLandmarkRefinerConfig(new StubBeatLandmarkRefiner(StubBeatLandmarkRefiner.Mode.NoOp));
            return true;
    }

    if (spec.StartsWith("onnx:", StringComparison.Ordinal))
    {
        string modelPath = spec["onnx:".Length..];
        if (!File.Exists(modelPath))
        {
            error = $"--landmark=onnx: model not found: '{modelPath}'";
            return false;
        }
        try
        {
            refiner = new BeatLandmarkRefinerConfig(new OnnxBeatLandmarkRefiner(modelPath));
            return true;
        }
        catch (Exception ex)
        {
            error = $"--landmark=onnx: failed to load '{modelPath}': {ex.Message}";
            return false;
        }
    }

    error = $"unknown --landmark value '{spec}' (off|stub:noop|stub:cpeak|onnx:<path>)";
    return false;
}

static IEnumerable<string> GenerateSyntheticFixtures(Dictionary<string, GeneratedFixtureExpectation> expectationsByFile)
{
    string dir = Path.Combine(Path.GetTempPath(), "timegrapher-verify-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    (int Bph, int SampleRate, int Seconds, double PcmPeak, double NoisePeak, double RateSPerDay, double BeatErrorMs, string Name)[] cases =
    {
        (18000, 48000, 10, 0.40, 0.00, 0.0, 0.0, "clean"),
        (21600, 48000, 10, 0.18, 0.02, 0.0, 0.0, "noisy-lowamp"),
        (28800, 96000, 8, 0.40, 0.00, 0.0, 0.0, "highrate"),
        (36000, 48000, 10, 0.35, 0.01, 0.0, 0.0, "edge"),
        (43200, 192000, 6, 0.35, 0.00, 0.0, 0.0, "max-standard-rate"),
        (21600, 48000, 12, 0.40, 0.00, 30.0, 0.0, "fast-plus30s"),
        (21600, 48000, 12, 0.40, 0.00, 0.0, 5.0, "beaterror5ms"),
    };

    foreach ((int bph, int sampleRate, int seconds, double pcmPeak, double noisePeak, double rateSPerDay, double beatErrorMs, string name) in cases)
    {
        string path = Path.Combine(dir, string.Format(
            CultureInfo.InvariantCulture,
            "{0}BPH_{1}_{2}Hz_generated.wav",
            bph,
            name,
            sampleRate));
        double[] truthTimes = WriteSyntheticWav(
            path, bph, sampleRate, seconds, pcmPeak, noisePeak, rateSPerDay: rateSPerDay, beatErrorMs: beatErrorMs);
        // These fixtures synthesize exact-timing watches (Clean config, no
        // jitter/wander), so the rate is gated even when the target is 0 s/d:
        // a regression that shifts a true-zero-rate watch off zero is caught.
        expectationsByFile[path] = new GeneratedFixtureExpectation(
            TruthTimes: truthTimes,
            ExpectedRateSPerDay: rateSPerDay,
            ExpectedBeatErrorMs: beatErrorMs == 0.0 ? null : -beatErrorMs,
            ExpectedDiffTicTacMs: beatErrorMs == 0.0 ? null : -beatErrorMs * 2.0,
            RateToleranceSPerDay: 0.5);
        yield return path;
    }

    (int Bph, int SampleRate, int Seconds, double PcmPeak, double NoisePeak, int SilenceLeadInSamples, bool Clip, bool Realistic, string Name)[] edgeCases =
    {
        (21600, 48000, 12, 0.30, 0.010, 0, false, true, "realistic-noisy"),
        (18000, 48000, 10, 0.95, 0.002, 0, true, false, "clipped"),
        (28800, 96000, 8, 0.35, 0.012, 0, false, true, "asymmetric-noisy"),
    };

    foreach ((int bph, int sampleRate, int seconds, double pcmPeak, double noisePeak, int silenceLeadInSamples, bool clip, bool realistic, string name) in edgeCases)
    {
        string path = Path.Combine(dir, string.Format(
            CultureInfo.InvariantCulture,
            "{0}BPH_{1}_{2}Hz_edge.wav",
            bph,
            name,
            sampleRate));
        expectationsByFile[path] = new GeneratedFixtureExpectation(
            WriteSyntheticWav(path, bph, sampleRate, seconds, pcmPeak, noisePeak, silenceLeadInSamples, clip, realistic));
        yield return path;
    }
}

static IEnumerable<string> GenerateByteBuiltFixtures(Dictionary<string, GeneratedFixtureExpectation> expectationsByFile)
{
    string dir = Path.Combine(Path.GetTempPath(), "timegrapher-verify-byte-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    (int Bph, int SampleRate, int Seconds, double PcmPeak, double NoisePeak, bool Extensible, bool OddJunk, bool ListChunk, string Name)[] cases =
    {
        (18000, 48000, 10, 0.40, 0.00, false, true, true, "riff-junk"),
        (21600, 48000, 10, 0.25, 0.01, true, true, false, "extensible"),
        (28800, 96000, 8, 0.35, 0.00, true, false, true, "extensible-list"),
    };

    foreach ((int bph, int sampleRate, int seconds, double pcmPeak, double noisePeak, bool extensible, bool oddJunk, bool listChunk, string name) in cases)
    {
        string path = Path.Combine(dir, string.Format(
            CultureInfo.InvariantCulture,
            "{0}BPH_{1}_{2}Hz_byte-fixture.wav",
            bph,
            name,
            sampleRate));
        expectationsByFile[path] = new GeneratedFixtureExpectation(
            WriteByteBuiltSyntheticWav(path, bph, sampleRate, seconds, pcmPeak, noisePeak, extensible, oddJunk, listChunk));
        yield return path;
    }
}

static double[] WriteSyntheticWav(
    string path,
    int bph,
    int sampleRate,
    int seconds,
    double pcmPeak,
    double noisePeak,
    int silenceLeadInSamples = 0,
    bool hardClip = false,
    bool realistic = false,
    double rateSPerDay = 0.0,
    double beatErrorMs = 0.0)
{
    WatchSynthStreamConfig synthConfig = realistic
        ? WatchSynthStreamConfig.Realistic()
        : WatchSynthStreamConfig.Clean();
    synthConfig.SampleRateHz = (uint)sampleRate;
    synthConfig.Bph = bph;
    synthConfig.NoisePeakSignalLevel = noisePeak;
    synthConfig.PcmPeakSignalLevel = pcmPeak;
    synthConfig.RateErrorSPerDay = rateSPerDay;
    synthConfig.BeatErrorMs = beatErrorMs;

    var synth = new WatchSynthStream(synthConfig);
    using var writer = new WavStreamWriter();
    if (!writer.Open(path, sampleRate, channels: 1))
    {
        throw new IOException("Failed to open generated WAV file: " + path);
    }

    var block = new float[4096];
    int silenceRemaining = silenceLeadInSamples;
    while (silenceRemaining > 0)
    {
        int slice = Math.Min(block.Length, silenceRemaining);
        Span<float> span = block.AsSpan(0, slice);
        span.Clear();
        if (!writer.Write(span))
        {
            throw new IOException("Failed to write generated WAV file: " + path);
        }
        silenceRemaining -= slice;
    }

    // Ground-truth beat times via the event side channel, shifted by the
    // silence lead-in so they are file-relative.
    var truthTimes = new List<double>();
    double leadInS = (double)silenceLeadInSamples / sampleRate;
    var eventBuf = new WatchSynthStreamEvent[64];

    int remaining = sampleRate * seconds;
    while (remaining > 0)
    {
        int slice = Math.Min(block.Length, remaining);
        Span<float> span = block.AsSpan(0, slice);
        WatchSynthStreamFillResult fill = synth.FillF32(span, eventBuf);
        for (int i = 0; i < fill.EventsWritten; i++)
        {
            truthTimes.Add(leadInS + eventBuf[i].TimeS);
        }
        if (hardClip)
        {
            for (int i = 0; i < slice; i++)
            {
                span[i] = Math.Clamp(span[i], -0.35f, 0.35f);
            }
        }
        if (!writer.Write(span))
        {
            throw new IOException("Failed to write generated WAV file: " + path);
        }
        remaining -= slice;
    }

    if (!writer.Close())
    {
        throw new IOException("Failed to close generated WAV file: " + path);
    }

    return truthTimes.ToArray();
}

static double[] WriteByteBuiltSyntheticWav(
    string path,
    int bph,
    int sampleRate,
    int seconds,
    double pcmPeak,
    double noisePeak,
    bool extensible,
    bool oddJunk,
    bool listChunk)
{
    WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
    synthConfig.SampleRateHz = (uint)sampleRate;
    synthConfig.Bph = bph;
    synthConfig.NoisePeakSignalLevel = noisePeak;
    synthConfig.PcmPeakSignalLevel = pcmPeak;

    var synth = new WatchSynthStream(synthConfig);
    int sampleCount = sampleRate * seconds;
    uint dataSize = checked((uint)(sampleCount * sizeof(float)));
    uint fmtSize = extensible ? 40u : 16u;
    uint junkPayloadSize = oddJunk ? 3u : 0u;
    uint junkChunkSize = oddJunk ? 8u + junkPayloadSize + 1u : 0u;
    uint listPayloadSize = listChunk ? 4u : 0u;
    uint listTotalSize = listChunk ? 8u + listPayloadSize : 0u;
    uint riffSize = 4u + 8u + fmtSize + junkChunkSize + listTotalSize + 8u + dataSize;

    using FileStream stream = File.Create(path);
    using var writer = new BinaryWriter(stream);

    WriteFourCc(writer, "RIFF");
    writer.Write(riffSize);
    WriteFourCc(writer, "WAVE");

    if (oddJunk)
    {
        WriteFourCc(writer, "JUNK");
        writer.Write(junkPayloadSize);
        writer.Write(new byte[] { 0x10, 0x20, 0x30 });
        writer.Write((byte)0);
    }

    WriteFourCc(writer, "fmt ");
    writer.Write(fmtSize);
    writer.Write(extensible ? WavProbe.WaveFormatExtensible : WavProbe.WaveFormatIeeeFloat);
    writer.Write((ushort)1);
    writer.Write((uint)sampleRate);
    writer.Write((uint)(sampleRate * sizeof(float)));
    writer.Write((ushort)sizeof(float));
    writer.Write((ushort)32);
    if (extensible)
    {
        writer.Write((ushort)22);
        writer.Write((ushort)32);
        writer.Write((uint)0);
        writer.Write(WavProbe.WaveFormatIeeeFloat);
        writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
    }

    if (listChunk)
    {
        WriteFourCc(writer, "LIST");
        writer.Write(listPayloadSize);
        WriteFourCc(writer, "INFO");
    }

    WriteFourCc(writer, "data");
    writer.Write(dataSize);

    var truthTimes = new List<double>();
    var eventBuf = new WatchSynthStreamEvent[64];
    var block = new float[4096];
    int remaining = sampleCount;
    while (remaining > 0)
    {
        int slice = Math.Min(block.Length, remaining);
        Span<float> span = block.AsSpan(0, slice);
        WatchSynthStreamFillResult fill = synth.FillF32(span, eventBuf);
        for (int i = 0; i < fill.EventsWritten; i++)
        {
            truthTimes.Add(eventBuf[i].TimeS);
        }
        for (int i = 0; i < slice; i++)
        {
            writer.Write(BitConverter.SingleToInt32Bits(span[i]));
        }
        remaining -= slice;
    }

    return truthTimes.ToArray();
}

static void WriteFourCc(BinaryWriter writer, string fourCc)
{
    writer.Write(new[] { (byte)fourCc[0], (byte)fourCc[1], (byte)fourCc[2], (byte)fourCc[3] });
}

sealed record GeneratedFixtureExpectation(
    double[] TruthTimes,
    double? ExpectedRateSPerDay = null,
    double? ExpectedBeatErrorMs = null,
    double? ExpectedDiffTicTacMs = null,
    double RateToleranceSPerDay = 1.0,
    double BeatErrorToleranceMs = 1.0,
    double DiffTicTacToleranceMs = 2.0);
