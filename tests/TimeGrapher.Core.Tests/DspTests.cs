using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Front-end DSP primitives feeding the burst detector: the DC-blocking high-pass and the
/// rectify+smooth envelope. Both are streaming single-pole filters with tiny per-sample state.
/// </summary>
public sealed class DspTests
{
    [Fact]
    public void Hpf_PassesFirstSampleThenBlocksDc()
    {
        var hpf = new TgHpf(48000, 200);
        var input = new float[2000];
        Array.Fill(input, 1.0f);
        var output = new float[input.Length];

        hpf.Process(input, output, input.Length);

        Assert.Equal(1.0f, output[0], 5);          // no prior state -> first sample passes through
        Assert.True(
            Math.Abs(output[^1]) < 1e-3f,
            $"sustained DC should decay toward zero, actual final sample was {output[^1]}");
    }

    [Fact]
    public void Hpf_ResetRestoresInitialResponse()
    {
        var hpf = new TgHpf(48000, 200);
        var dc = new float[500];
        Array.Fill(dc, 1.0f);
        hpf.Process(dc, new float[dc.Length], dc.Length);

        hpf.Reset();
        var output = new float[1];
        hpf.Process(new float[] { 1.0f }, output, 1);

        Assert.Equal(1.0f, output[0], 5);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Hpf_NonFiniteCutoffDoesNotPoisonOutput(double cutoff)
    {
        // A non-finite cutoff used to bypass both range clamps (fc<1 / fc>0.25fs
        // are false for NaN) and make the pole coefficient NaN, turning every
        // filtered sample NaN and silently killing detection. The Init guard now
        // folds non-finite to the low clamp.
        var hpf = new TgHpf(48000, cutoff);
        var input = new float[200];
        Array.Fill(input, 0.5f);
        var output = new float[input.Length];

        hpf.Process(input, output, input.Length);

        Assert.All(output, v => Assert.True(float.IsFinite(v), $"non-finite cutoff produced {v}"));
    }

    [Fact]
    public void Envelope_RectifiesAndConvergesToMagnitude()
    {
        var env = new TgEnvelope(48000, 0.15);
        var input = new float[5000];
        Array.Fill(input, -1.0f); // negative input must be rectified
        var output = new float[input.Length];

        env.Process(input, output, input.Length);

        Assert.All(output, v => Assert.True(v >= 0.0f, $"envelope emitted a negative sample {v}"));
        Assert.True(
            Math.Abs(output[^1] - 1.0f) < 1e-3f,
            $"envelope should converge to |x| = 1, actual final sample was {output[^1]}");
    }

    [Fact]
    public void Envelope_RisesGraduallyFromZero()
    {
        var env = new TgEnvelope(48000, 1.0);
        var output = new float[3];
        env.Process(new[] { 1.0f, 1.0f, 1.0f }, output, 3);

        Assert.True(
            output[0] > 0.0f && output[0] < 1.0f,
            $"first smoothed step should be between 0 and 1, actual value was {output[0]}");
        Assert.True(output[1] > output[0], $"second sample {output[1]} should exceed first {output[0]}");
        Assert.True(output[2] > output[1], $"third sample {output[2]} should exceed second {output[1]}");
    }
}
