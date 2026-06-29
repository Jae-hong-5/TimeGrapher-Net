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
    public void AxisOrder_IsTheEightVerticalPositions()
    {
        Assert.Equal(8, WatchHealthRadarModel.AxisOrder.Count);
        // The octagon is the vertical (hanging) positions only — no flat CH/CB.
        Assert.DoesNotContain(WatchHealthRadarModel.AxisOrder, p => p.IsHorizontal());
        // The 45° intermediate positions are now included to complete the octagon.
        Assert.Contains(WatchPosition.P3H45, WatchHealthRadarModel.AxisOrder);
        // 12H sits at the top, clockwise, so each vertex matches the clock face.
        Assert.Equal(WatchPosition.P12H, WatchHealthRadarModel.AxisOrder[0]);
    }

    [Fact]
    public void Build_WithNoPositions_IsEmptyAndPending()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(new PositionSummary[0], RadarMetric.Amplitude);

        Assert.Equal(8, model.Axes.Count);
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
    public void Build_CountsOnlyMeasuredVerticalAxes()
    {
        var positions = new[]
        {
            Amp(WatchPosition.P12H, 290),
            Amp(WatchPosition.P3H, 285),
            Amp(WatchPosition.P6H, 270),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(3, model.MeasuredCount);
        Assert.True(AxisFor(model, WatchPosition.P12H).HasValue);
        Assert.False(AxisFor(model, WatchPosition.P9H).HasValue);
    }

    [Fact]
    public void Amplitude_HigherValue_MapsToLargerRadius()
    {
        var positions = new[]
        {
            Amp(WatchPosition.P12H, 300),
            Amp(WatchPosition.P6H, 250),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.True(AxisFor(model, WatchPosition.P12H).RadiusFraction > AxisFor(model, WatchPosition.P6H).RadiusFraction);
    }

    [Fact]
    public void Amplitude_HasHealthyBandWithinTheScale()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(
            new[] { Amp(WatchPosition.P12H, 290) }, RadarMetric.Amplitude);

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
    public void Amplitude_HorizontalServiceLow_RaisesAlertAndIsWeakest()
    {
        // A flat-position (dial-down) service-low amplitude must drive the verdict
        // and be picked as weakest even though CB is not a radar vertex.
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
        var positions = new[] { Amp(WatchPosition.P12H, inBand, count: 5) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(1, model.MeasuredCount);
        Assert.True(AxisFor(model, WatchPosition.P12H).HasValue);
        Assert.Equal(VarioVerdictLevel.Pending, model.VerdictLevel);
    }

    [Fact]
    public void BeatError_LargerMagnitude_MapsToLargerRadius()
    {
        var positions = new[]
        {
            Beat(WatchPosition.P12H, 0.5),
            Beat(WatchPosition.P6H, 3.0),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.BeatError);

        Assert.True(AxisFor(model, WatchPosition.P6H).RadiusFraction > AxisFor(model, WatchPosition.P12H).RadiusFraction);
        Assert.Equal(WatchPosition.P6H, model.WeakestPosition);
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
            new[] { Rate(WatchPosition.P12H, 8.0) }, RadarMetric.Rate);

        Assert.Contains("s/d", AxisFor(model, WatchPosition.P12H).ValueText);
    }

    private static PositionSummary AmpRate(WatchPosition position, double amplitude, double rate, long count = 50) =>
        new(position, Stat(rate, count), Stat(amplitude, count), None);

    private static HealthLevelRow LevelFor(WatchHealthRadarModel model, WatchPosition position) =>
        model.Levels.Single(l => l.Position == position);

    private static HealthHorizontalRow HorizontalFor(WatchHealthRadarModel model, WatchPosition position) =>
        model.Horizontal.Single(h => h.Position == position);

    [Fact]
    public void Build_PopulatesLevelsForEveryAxisPosition()
    {
        var positions = new[] { Amp(WatchPosition.P12H, 290), Amp(WatchPosition.P3H, 285) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(WatchHealthRadarModel.AxisOrder.Count, model.Levels.Count);
        Assert.True(LevelFor(model, WatchPosition.P12H).HasValue);
        Assert.Contains("290", LevelFor(model, WatchPosition.P12H).AmplitudeText);
        Assert.False(LevelFor(model, WatchPosition.P9H).HasValue);
    }

    [Fact]
    public void Build_LevelSeverityIsWorstOfTheThreeMeasures()
    {
        // Service-low amplitude alone makes the row's status dot ALERT (Bad).
        double serviceLow = ServiceLowAmplitude();
        var positions = new[] { Amp(WatchPosition.P12H, serviceLow) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(VarioVerdictLevel.Bad, LevelFor(model, WatchPosition.P12H).Level);
    }

    [Fact]
    public void Build_PopulatesHorizontalRowsForChAndCb()
    {
        var positions = new[] { Amp(WatchPosition.CH, 290), Amp(WatchPosition.P12H, 280) };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.Equal(WatchHealthRadarModel.HorizontalOrder.Count, model.Horizontal.Count);
        Assert.True(HorizontalFor(model, WatchPosition.CH).HasValue);
        Assert.False(HorizontalFor(model, WatchPosition.CB).HasValue);
        Assert.Contains("290", HorizontalFor(model, WatchPosition.CH).ValueText);
        // CH/CB are reported as horizontal rows, never as radar vertices.
        Assert.DoesNotContain(model.Axes, a => a.Position == WatchPosition.CH);
    }

    [Fact]
    public void Horizontal_OutOfBandFlagsTheMeanDot()
    {
        double inBand = InBandAmplitude();
        double serviceLow = ServiceLowAmplitude();
        var positions = new[]
        {
            Amp(WatchPosition.CH, inBand),
            Amp(WatchPosition.CB, serviceLow),
        };

        WatchHealthRadarModel model = WatchHealthRadarModel.Build(positions, RadarMetric.Amplitude);

        Assert.False(HorizontalFor(model, WatchPosition.CH).OutOfBand);
        Assert.True(HorizontalFor(model, WatchPosition.CB).OutOfBand);
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
