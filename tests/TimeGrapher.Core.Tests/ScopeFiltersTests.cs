using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Multi-Filter Scope filter bank semantics on closed-form sequences: F0 mirrors
/// excursions around the running mean, F1 is the ring-buffer moving average of
/// F0, F2/F3 keep rising slopes and suppress falling ones via the decaying
/// attenuator, and F3 sees only the upper portion relative to the average.
/// </summary>
public sealed class ScopeFiltersTests
{
    // ---- RisingEdgeEmphasis (the F2/F3 formulation), decay 0.5 per sample for
    // ---- exact closed-form arithmetic.

    private static double[] Emphasize(double fallDecay, params double[] input)
    {
        var edge = new RisingEdgeEmphasis(fallDecay);
        var output = new double[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = edge.Process(input[i]);
        }

        return output;
    }

    [Fact]
    public void Emphasis_RisingRampPassesThroughUnchanged()
    {
        double[] ramp = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
        Assert.Equal(ramp, Emphasize(0.5, ramp));
    }

    [Fact]
    public void Emphasis_StepKeepsItsPlateau()
    {
        // Plateau samples (value == previous) count as rising and pass through.
        Assert.Equal(new[] { 0.0, 1.0, 1.0, 1.0 }, Emphasize(0.5, 0.0, 1.0, 1.0, 1.0));
    }

    [Fact]
    public void Emphasis_ImpulseFallIsFullySuppressed()
    {
        // After the impulse the attenuator sits at the peak (1.0), so the fall
        // back to zero outputs max(0 - 0.5, 0) = 0.
        Assert.Equal(new[] { 0.0, 1.0, 0.0, 0.0, 0.0 }, Emphasize(0.5, 0.0, 1.0, 0.0, 0.0, 0.0));
    }

    [Fact]
    public void Emphasis_FallingRampIsAttenuatedByTheDecayingAttenuator()
    {
        // Peak 1.0 arms the attenuator; each falling sample decays it by 0.5 and
        // outputs max(value - attenuator, 0): 0.8-0.5, 0.6-0.25, 0.4-0.125.
        double[] output = Emphasize(0.5, 1.0, 0.8, 0.6, 0.4);

        Assert.Equal(1.0, output[0], 12);
        Assert.Equal(0.3, output[1], 12);
        Assert.Equal(0.35, output[2], 12);
        Assert.Equal(0.275, output[3], 12);
    }

    [Fact]
    public void Emphasis_NextRiseReArmsAndPassesThrough()
    {
        // The fall to 0.2 is clipped to zero; the following rise to 0.9 passes
        // through unchanged (rises stay prominent after any decay).
        Assert.Equal(new[] { 1.0, 0.0, 0.9 }, Emphasize(0.5, 1.0, 0.2, 0.9));
    }

    [Fact]
    public void Emphasis_ResetClearsTheArmedAttenuator()
    {
        var edge = new RisingEdgeEmphasis(0.5);
        edge.Process(1.0);
        edge.Reset();

        // Post-reset the first sample is a rise from zero again.
        Assert.Equal(0.4, edge.Process(0.4), 12);
    }

    // ---- ScopeFilters composite bank.

    [Fact]
    public void F0_ConstantInputSettlesToZeroImmediately()
    {
        // The running mean primes on the first sample, so a constant input has
        // no startup transient: every view reads zero.
        var filters = new ScopeFilters(48000);
        for (int i = 0; i < 100; i++)
        {
            ScopeFilterSample sample = filters.Process(0.3);
            Assert.Equal(0.0, sample.F0);
            Assert.Equal(0.0, sample.F1);
            Assert.Equal(0.0, sample.F2);
            Assert.Equal(0.0, sample.F3);
        }
    }

    [Fact]
    public void F0_MirrorsPositiveAndNegativeExcursionsSymmetrically()
    {
        var positive = new ScopeFilters(48000);
        var negative = new ScopeFilters(48000);
        positive.Process(0.0);
        negative.Process(0.0);

        double upper = positive.Process(0.5).F0;
        double lower = negative.Process(-0.5).F0;

        Assert.True(upper > 0.4);
        Assert.Equal(upper, lower, 12);
    }

    [Theory]
    [InlineData(8000, 4)]
    [InlineData(48000, 24)]
    public void F1_IsTheMovingAverageOfTheLastWindowOfF0(int sampleRate, int expectedWindow)
    {
        Assert.Equal(expectedWindow, ScopeFilters.SmoothingWindowLength(sampleRate));

        var filters = new ScopeFilters(sampleRate);
        double[] input = { 0.0, 0.4, -0.2, 0.6, -0.5, 0.1, 0.3, -0.7, 0.2, 0.0, 0.5, -0.3 };
        var f0History = new List<double>();
        for (int i = 0; i < input.Length; i++)
        {
            ScopeFilterSample sample = filters.Process(input[i]);
            f0History.Add(sample.F0);

            // Warm-up averages over the samples seen so far, then the window.
            int windowStart = Math.Max(0, f0History.Count - expectedWindow);
            double expected = f0History.Skip(windowStart).Average();
            Assert.Equal(expected, sample.F1, 12);
        }
    }

    [Fact]
    public void F2_EqualsF1OnRisesAndNeverExceedsIt()
    {
        var filters = new ScopeFilters(8000);
        double[] input = { 0.0, 0.4, -0.2, 0.9, -0.8, 0.05, 0.3, -0.7, 0.0, 0.1, 0.6, -0.6 };
        double previousF1 = 0.0;
        foreach (double x in input)
        {
            ScopeFilterSample sample = filters.Process(x);
            if (sample.F1 >= previousF1)
            {
                Assert.Equal(sample.F1, sample.F2, 12);
            }
            else
            {
                Assert.True(sample.F2 < sample.F1);
            }

            Assert.True(sample.F2 <= sample.F1 + 1e-12);
            previousF1 = sample.F1;
        }
    }

    [Fact]
    public void F3_SeesOnlyTheUpperPortionRelativeToTheAverage()
    {
        // A negative excursion is invisible to F3 (lower portion clipped) even
        // though F0 mirrors it; a positive excursion passes as a rising edge.
        var negative = new ScopeFilters(48000);
        negative.Process(0.0);
        ScopeFilterSample below = negative.Process(-0.8);
        Assert.True(below.F0 > 0.7);
        Assert.Equal(0.0, below.F3);

        var positive = new ScopeFilters(48000);
        positive.Process(0.0);
        ScopeFilterSample above = positive.Process(0.8);
        Assert.True(above.F3 > 0.7);
    }

    [Fact]
    public void Reset_RestoresTheUnprimedInitialState()
    {
        var filters = new ScopeFilters(48000);
        for (int i = 0; i < 50; i++)
        {
            filters.Process(i % 2 == 0 ? 0.9 : -0.9);
        }

        filters.Reset();

        // Mean re-primes on the first post-reset sample: constant input is flat.
        Assert.Equal(0.0, filters.Process(0.3).F0);
        Assert.Equal(0.0, filters.Process(0.3).F1);
    }
}
