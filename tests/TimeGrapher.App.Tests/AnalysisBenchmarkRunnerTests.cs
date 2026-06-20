using TimeGrapher.App.Audio;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AnalysisBenchmarkRunnerTests
{
    private static (int ExitCode, string Output) RunCapturingStdout(string[] args)
    {
        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = AnalysisBenchmarkRunner.Run(args);
            return (exit, sw.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void RunCompletesShortSyntheticBenchmark()
    {
        (int exitCode, string output) = RunCapturingStdout(
            new[] { "--analysis-benchmark", "--bph", "43200", "--rate", "48000", "--duration-ms", "3000" });

        Assert.Equal(0, exitCode);
        // The runner's evidence surface, not just the exit code: the summary must
        // report the configured + detected BPH and the budget line must carry the
        // latency/deadline fields.
        Assert.Contains("expected_bph=43200", output);
        Assert.Contains("detected_bph=43200", output);
        Assert.Contains("max_lag_ms=", output);
        Assert.Contains("max_deadline_level=", output);
    }

    [Fact]
    public void RunCompletesWavBenchmark()
    {
        string path = Path.Combine(Path.GetTempPath(), "43200BPH_benchmark_" + Guid.NewGuid().ToString("N") + ".wav");

        try
        {
            WriteSyntheticWav(path, bph: 43200, sampleRate: 48000, durationMs: 3000);

            (int exitCode, string output) = RunCapturingStdout(
                new[] { "--analysis-benchmark", "--wav", path });

            Assert.Equal(0, exitCode);
            // Expected BPH is parsed from the filename ("43200BPH_..."); assert it was
            // parsed (not 0) and detected, so a filename-parsing regression fails here.
            Assert.Contains("expected_bph=43200", output);
            Assert.Contains("detected_bph=43200", output);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteSyntheticWav(string path, int bph, int sampleRate, int durationMs)
    {
        WatchSynthStreamConfig config = WatchSynthStreamConfig.Realistic();
        config.SampleRateHz = (uint)sampleRate;
        config.Bph = bph;
        config.PcmPeakSignalLevel = 0.35;

        var synth = new WatchSynthStream(config);
        using var writer = new WavStreamWriter();
        Assert.True(writer.Open(path, sampleRate, channels: 1));

        var block = new float[4096];
        int remaining = (int)Math.Ceiling(sampleRate * (durationMs / 1000.0));
        while (remaining > 0)
        {
            int count = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, count);
            synth.Generate(span);
            Assert.True(writer.Write(span));
            remaining -= count;
        }

        Assert.True(writer.Close());
    }
}
