using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The shared strip-lane sampling policy behind the Beat-Noise strips and the
/// Waveform-Compare lanes: max-decimate to a point budget with stride =
/// max(1, length / budget) (integer division, tail samples beyond the last
/// full bucket are dropped) and normalize each bucket max to the segment's
/// own peak (1 when the segment is silent, so silence never divides by zero).
/// </summary>
public sealed class EnvelopeLaneSamplerTests
{
    private sealed record EmittedPoint(int Point, int PointCount, int Stride, double NormalizedValue);

    private static List<EmittedPoint> Sample(float[] samples, int pointBudget)
    {
        var points = new List<EmittedPoint>();
        EnvelopeLaneSampler.MaxDecimateNormalized(
            samples, pointBudget,
            (point, pointCount, stride, normalizedValue) =>
                points.Add(new EmittedPoint(point, pointCount, stride, normalizedValue)));
        return points;
    }

    [Fact]
    public void EmptySpanEmitsNothing()
    {
        Assert.Empty(Sample(Array.Empty<float>(), pointBudget: 8));
    }

    [Fact]
    public void AllZeroSamplesNormalizeAgainstThePeakOneFallback()
    {
        List<EmittedPoint> points = Sample(new float[] { 0f, 0f, 0f }, pointBudget: 8);

        // 0 / 1 (peak fallback), not 0 / 0 = NaN.
        Assert.Equal(3, points.Count);
        Assert.All(points, point => Assert.Equal(0.0, point.NormalizedValue));
    }

    [Fact]
    public void LengthBelowBudgetEmitsOnePointPerSample()
    {
        List<EmittedPoint> points = Sample(new float[] { 1f, 4f, 2f }, pointBudget: 8);

        // Stride clamps at 1, every bucket holds one sample, normalized to the
        // peak of 4 (exact binary fractions, so Equal is safe on doubles).
        Assert.Equal(
            new[]
            {
                new EmittedPoint(0, 3, 1, 0.25),
                new EmittedPoint(1, 3, 1, 1.0),
                new EmittedPoint(2, 3, 1, 0.5),
            },
            points);
    }

    [Fact]
    public void LengthEqualToBudgetKeepsStrideOne()
    {
        List<EmittedPoint> points = Sample(new float[] { 1f, 2f, 4f, 2f }, pointBudget: 4);

        Assert.Equal(4, points.Count);
        Assert.All(points, point => Assert.Equal(1, point.Stride));
        Assert.Equal(new[] { 0.25, 0.5, 1.0, 0.5 }, points.Select(point => point.NormalizedValue));
    }

    [Fact]
    public void LengthOverBudgetBucketsByStrideAndTruncatesTheTail()
    {
        // 11 samples into a budget of 4: stride = 11 / 4 = 2 and
        // points = 11 / 2 = 5 (the decimation may overshoot the budget); the
        // 11th sample falls past the last full bucket and is dropped.
        float[] samples = { 1f, 0f, 2f, 0f, 4f, 0f, 8f, 0f, 16f, 0f, 0.5f };

        List<EmittedPoint> points = Sample(samples, pointBudget: 4);

        Assert.Equal(5, points.Count);
        Assert.All(points, point => Assert.Equal(2, point.Stride));
        Assert.All(points, point => Assert.Equal(5, point.PointCount));
        // Bucket maxes {1, 2, 4, 8, 16} against the peak of 16.
        Assert.Equal(
            new[] { 0.0625, 0.125, 0.25, 0.5, 1.0 },
            points.Select(point => point.NormalizedValue));
    }

    [Fact]
    public void SinglePointBudgetFoldsTheWholeSegmentIntoOneBucket()
    {
        List<EmittedPoint> points = Sample(new float[] { 1f, 8f, 4f, 2f }, pointBudget: 1);

        // PointCount can reach 1, so callers mapping point / (pointCount - 1)
        // into their own x-space must handle the single-point division.
        EmittedPoint point = Assert.Single(points);
        Assert.Equal(new EmittedPoint(0, 1, 4, 1.0), point);
    }
}
