using System.Linq;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public class WatchHealthRadarModelTests
{
    private static StatsSummary Stat(double mean, long count = 50) => new(true, mean, mean, mean, 0.0, count);

    private static StatsSummary None => default;

    private static PositionSummary Amp(WatchPosition position, double amplitude, long count = 50) =>
        new(position, None, Stat(amplitude, count), None);

    private static PositionSummary Rate(WatchPosition position, double rate, long count = 50) =>
        new(position, Stat(rate, count), None, None);

    private static PositionSummary Beat(WatchPosition position, double beatMs, long count = 50) =>
        new(position, None, None, Stat(beatMs, count));

    private static RadarAxis AxisFor(WatchHealthRadarModel model, WatchPosition position) =>
        model.Axes.Single(a => a.Position == position);

    [Fact]
    public void AxisOrder_IsTheSixCardinalPositions()
    {
        Assert.Equal(6, WatchHealthRadarModel.AxisOrder.Count);
        Assert.DoesNotContain(WatchHealthRadarModel.AxisOrder, p => p.IsIntermediate());
        Assert.Contains(WatchPosition.CH, WatchHealthRadarModel.AxisOrder);
        Assert.Contains(WatchPosition.CB, WatchHealthRadarModel.AxisOrder);
    }

    [Fact]
    public void Build_WithNoPositions_IsEmptyAndPending()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(new PositionSummary[0], RadarMetric.Amplitude);

        Assert.Equal(6, model.Axes.Count);
        Assert.All(model.Axes, axis => Assert.False(axis.HasValue));
        Assert.Equal(0, model.MeasuredCount);
        Assert.Null(model.WeakestPosition);
        Assert.Equal(VarioVerdictLevel.Pending, model.VerdictLevel);
    }

    [Fact]
    public void Build_AxesFollowAxisOrder()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(new PositionSummary[0], RadarMetric.Amplitude);

        Assert.Equal(WatchHealthRadarModel.AxisOrder.ToArray(), model.Axes.Select(a => a.Position).ToArray());
    }

    [Fact]
    public void Build_CountsOnlyMeasuredPositions()
    {
        var positions = new[]
        {
            Amp(WatchPosition.CH, 290),
            Amp(WatchPosition.CB, 285),
            Amp(WatchPosition.P3H, 270),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(3, model.MeasuredCount);
        Assert.True(AxisFor(model, WatchPosition.CH).HasValue);
        Assert.False(AxisFor(model, WatchPosition.P9H).HasValue);
    }

    [Fact]
    public void Amplitude_HigherValue_MapsToLargerRadius()
    {
        var positions = new[]
        {
            Amp(WatchPosition.CH, 300),
            Amp(WatchPosition.CB, 250),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.True(AxisFor(model, WatchPosition.CH).RadiusFraction > AxisFor(model, WatchPosition.CB).RadiusFraction);
    }

    [Fact]
    public void Amplitude_HasHealthyBandWithinTheScale()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(
            new[] { Amp(WatchPosition.CH, 290) }, RadarMetric.Amplitude);

        Assert.True(model.HasBand);
        Assert.InRange(model.BandInnerFraction, 0.0, 1.0);
        Assert.InRange(model.BandOuterFraction, 0.0, 1.0);
        Assert.True(model.BandOuterFraction > model.BandInnerFraction);
    }

    [Fact]
    public void Amplitude_AllInBand_ReadsHealthy()
    {
        double inBand = (VarioGaugePolicy.AmplitudeAcceptMinDeg + VarioGaugePolicy.AmplitudeAcceptMaxDeg) / 2.0;
        var positions = WatchHealthRadarModel.AxisOrder.Select(p => Amp(p, inBand)).ToArray();

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(VarioVerdictLevel.Good, model.VerdictLevel);
        Assert.Contains("OK", model.VerdictText);
    }

    [Fact]
    public void Amplitude_BelowServiceThreshold_RaisesAlert()
    {
        double inBand = (VarioGaugePolicy.AmplitudeAcceptMinDeg + VarioGaugePolicy.AmplitudeAcceptMaxDeg) / 2.0;
        double serviceLow = VarioVerdict.AmplitudeServiceDeg - 20.0;
        var positions = new[]
        {
            Amp(WatchPosition.CH, inBand),
            Amp(WatchPosition.CB, serviceLow),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(VarioVerdictLevel.Bad, model.VerdictLevel);
        Assert.Contains("ALERT", model.VerdictText);
        Assert.Equal(WatchPosition.CB, model.WeakestPosition);
    }

    [Fact]
    public void Verdict_StaysPendingUntilWarmupCountReached()
    {
        double inBand = (VarioGaugePolicy.AmplitudeAcceptMinDeg + VarioGaugePolicy.AmplitudeAcceptMaxDeg) / 2.0;
        var positions = new[] { Amp(WatchPosition.CH, inBand, count: 5) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(1, model.MeasuredCount);
        Assert.True(AxisFor(model, WatchPosition.CH).HasValue);
        Assert.Equal(VarioVerdictLevel.Pending, model.VerdictLevel);
    }

    [Fact]
    public void BeatError_LargerMagnitude_MapsToLargerRadius()
    {
        var positions = new[]
        {
            Beat(WatchPosition.CH, 0.5),
            Beat(WatchPosition.CB, 3.0),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.BeatError);

        Assert.True(AxisFor(model, WatchPosition.CB).RadiusFraction > AxisFor(model, WatchPosition.CH).RadiusFraction);
        Assert.Equal(WatchPosition.CB, model.WeakestPosition);
    }

    [Fact]
    public void Rate_FormatsSignedValueAndSelectsMetricTitle()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(
            new[] { Rate(WatchPosition.CH, 8.0) }, RadarMetric.Rate);

        Assert.Contains("Rate", model.MetricTitle);
        Assert.Contains("s/d", AxisFor(model, WatchPosition.CH).ValueText);
    }
}
