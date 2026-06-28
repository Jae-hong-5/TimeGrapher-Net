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

    private static double InBandAmplitude() =>
        (VarioGaugePolicy.AmplitudeAcceptMinDeg + VarioGaugePolicy.AmplitudeAcceptMaxDeg) / 2.0;

    private static double ServiceLowAmplitude() => VarioVerdict.AmplitudeServiceDeg - 20.0;

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

    public static TheoryData<string, PositionSummary[], WatchPosition, int, string> OverallGuideCases() => new()
    {
        { "Measuring", Array.Empty<PositionSummary>(), WatchPosition.CH, (int)VarioVerdictLevel.Pending, "Measuring…" },
        { "OK", new[] { Amp(WatchPosition.CH, InBandAmplitude()) }, WatchPosition.CH, (int)VarioVerdictLevel.Good, "OK — In Range" },
        {
            "WATCH",
            new[]
            {
                AmpRate(WatchPosition.CH, InBandAmplitude(), rate: 0.0),
                AmpRate(WatchPosition.P3H, InBandAmplitude(), rate: 0.0),
                AmpRate(WatchPosition.P9H, InBandAmplitude(), rate: 20.0),
            },
            WatchPosition.CH,
            (int)VarioVerdictLevel.Warn,
            "WATCH — Review"
        },
        { "ALERT", new[] { Amp(WatchPosition.CB, ServiceLowAmplitude()) }, WatchPosition.CB, (int)VarioVerdictLevel.Bad, "ALERT — Review Required" },
    };

    [Theory]
    [MemberData(nameof(OverallGuideCases))]
    public void Build_OverallGuideTextCoversEveryHealthState(
        string _,
        PositionSummary[] positions,
        WatchPosition activePosition,
        int expectedLevel,
        string expectedText)
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude, activePosition);

        Assert.Equal(expectedLevel, (int)model.OverallLevel);
        Assert.Equal(expectedText, model.OverallText);
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
        double inBand = InBandAmplitude();
        var positions = WatchHealthRadarModel.AxisOrder.Select(p => Amp(p, inBand)).ToArray();

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(VarioVerdictLevel.Good, model.VerdictLevel);
        Assert.Equal("OK — In Range", model.VerdictText);
    }

    [Fact]
    public void Amplitude_BelowServiceThreshold_RaisesAlert()
    {
        double inBand = InBandAmplitude();
        double serviceLow = ServiceLowAmplitude();
        var positions = new[]
        {
            Amp(WatchPosition.CH, inBand),
            Amp(WatchPosition.CB, serviceLow),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(VarioVerdictLevel.Bad, model.VerdictLevel);
        Assert.Equal("ALERT — Review Required", model.VerdictText);
        Assert.DoesNotContain("Service", model.VerdictText);
        Assert.Equal(WatchPosition.CB, model.WeakestPosition);
    }

    [Fact]
    public void Verdict_StaysPendingUntilWarmupCountReached()
    {
        double inBand = InBandAmplitude();
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
    public void BeatError_VerdictTracksTheEditedAcceptBand()
    {
        // Prove the verdict reads the live AcceptBandSettings, not a hardcoded threshold.
        // With a non-default 0.5 ms band, 0.75 ms (1.5x band) is Marginal and 1.5 ms (3x)
        // is High -- but under the old hardcoded 1/2 ms convention (or the default 0.8 ms
        // band) those same inputs would read Good and Marginal, so this only passes if the
        // radar grades against the edited band. Save/restore Current per the renderer-test
        // pattern (VarioRendererThemeTests).
        AcceptBandSettings original = AcceptBandSettings.Current;
        try
        {
            AcceptBandSettings.Current = AcceptBandSettings.Default with { BeatErrorMagnitudeMs = 0.5 };

            WatchHealthRadarModel good = WatchHealthRadarModel.Build(
                WatchHealthRadarModel.AxisOrder.Select(p => Beat(p, 0.25)).ToArray(), RadarMetric.BeatError);
            Assert.Equal(VarioVerdictLevel.Good, good.VerdictLevel);

            WatchHealthRadarModel marginal = WatchHealthRadarModel.Build(
                WatchHealthRadarModel.AxisOrder.Select(p => Beat(p, 0.75)).ToArray(), RadarMetric.BeatError);
            Assert.Equal(VarioVerdictLevel.Warn, marginal.VerdictLevel);

            WatchHealthRadarModel bad = WatchHealthRadarModel.Build(
                WatchHealthRadarModel.AxisOrder.Select(p => Beat(p, 1.5)).ToArray(), RadarMetric.BeatError);
            Assert.Equal(VarioVerdictLevel.Bad, bad.VerdictLevel);
        }
        finally
        {
            AcceptBandSettings.Current = original;
        }
    }

    [Fact]
    public void Rate_FormatsSignedValue()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(
            new[] { Rate(WatchPosition.CH, 8.0) }, RadarMetric.Rate);

        Assert.Contains("s/d", AxisFor(model, WatchPosition.CH).ValueText);
    }

    private static PositionSummary AmpRate(WatchPosition position, double amplitude, double rate, long count = 50) =>
        new(position, Stat(rate, count), Stat(amplitude, count), None);

    private static HealthLevelRow LevelFor(WatchHealthRadarModel model, WatchPosition position) =>
        model.Levels.Single(l => l.Position == position);

    [Fact]
    public void Build_PopulatesLevelsForEveryAxisPosition()
    {
        var positions = new[] { Amp(WatchPosition.CH, 290), Amp(WatchPosition.CB, 285) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(WatchHealthRadarModel.AxisOrder.Count, model.Levels.Count);
        Assert.True(LevelFor(model, WatchPosition.CH).HasValue);
        Assert.Contains("290", LevelFor(model, WatchPosition.CH).AmplitudeText);
        Assert.False(LevelFor(model, WatchPosition.P9H).HasValue);
    }

    [Fact]
    public void Build_LevelSeverityIsWorstOfTheThreeMeasures()
    {
        // Service-low amplitude alone makes the row's status dot ALERT (Bad).
        double serviceLow = ServiceLowAmplitude();
        var positions = new[] { Amp(WatchPosition.CH, serviceLow) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(VarioVerdictLevel.Bad, LevelFor(model, WatchPosition.CH).Level);
    }

    [Fact]
    public void Build_OverallEscalatesWhenConsistencyChecks()
    {
        // Amplitude in band on every measured position (band axis Good), but the
        // qualified rate spread is 20 s/d (> 15) so consistency CHECKs — the
        // overall verdict must take the worse axis (WATCH).
        double inBand = InBandAmplitude();
        var positions = new[]
        {
            AmpRate(WatchPosition.CH, inBand, rate: 0.0),
            AmpRate(WatchPosition.P3H, inBand, rate: 0.0),
            AmpRate(WatchPosition.P9H, inBand, rate: 20.0),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude, WatchPosition.CH);

        Assert.Equal(VarioVerdictLevel.Good, model.VerdictLevel);   // band axis unchanged
        Assert.Equal(VarioVerdictLevel.Warn, model.Consistency.Level);
        Assert.Equal(VarioVerdictLevel.Warn, model.OverallLevel);
        Assert.Equal("WATCH — Review", model.OverallText);
        Assert.DoesNotContain("Keep Measuring", model.OverallText);
    }

    [Fact]
    public void Build_OverallFollowsBandWhileConsistencyIsPending()
    {
        // One in-band amplitude position: band axis Good, consistency still
        // collecting (no rate / too few positions) — overall stays Good.
        double inBand = InBandAmplitude();
        var positions = new[] { Amp(WatchPosition.CH, inBand) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude, WatchPosition.CH);

        Assert.Equal(VarioVerdictLevel.Pending, model.Consistency.Level);
        Assert.Equal(VarioVerdictLevel.Good, model.OverallLevel);
    }
}
