using System;
using System.Collections.Generic;
using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>Which per-position measure the Watch Health radar plots.</summary>
internal enum RadarMetric
{
    Amplitude,
    Rate,
    BeatError,
}

/// <summary>
/// One radar axis: a watch position and the plotted value of the selected measure.
/// <see cref="RadiusFraction"/> is 0..1 (center..rim) so the control only scales it
/// to pixels; <see cref="HasValue"/> is false until that position recorded a sample.
/// </summary>
internal sealed record RadarAxis(
    WatchPosition Position,
    string Label,
    bool HasValue,
    double Value,
    string ValueText,
    double RadiusFraction,
    long Beats);

/// <summary>
/// Pure model behind the Watch Health radar. It maps the per-position aggregates
/// the frame snapshot already carries (<see cref="PositionSummary"/>) onto the six
/// fixed cardinal NIHS positions, normalising the selected measure onto a 0..1
/// radius, and reuses the shared accept band (<see cref="VarioGaugePolicy"/>) as the
/// healthy ring and <see cref="VarioVerdict"/> for the plain-language verdict. It
/// holds no Avalonia types, so the mapping is unit-testable without a window.
/// </summary>
internal sealed record WatchHealthRadarModel(
    RadarMetric Metric,
    string MetricTitle,
    string BetterHint,
    IReadOnlyList<RadarAxis> Axes,
    bool HasBand,
    double BandInnerFraction,
    double BandOuterFraction,
    IReadOnlyList<string> ScaleTicks,
    int MeasuredCount,
    WatchPosition? WeakestPosition,
    string SummaryLine,
    string VerdictText,
    VarioVerdictLevel VerdictLevel)
{
    /// <summary>
    /// The six cardinal NIHS positions, clockwise from the top (CH). The four 45°
    /// intermediate positions are intentionally excluded so the polygon stays a
    /// clean hexagon, matching how a movement is normally certified.
    /// </summary>
    public static readonly IReadOnlyList<WatchPosition> AxisOrder = new[]
    {
        WatchPosition.CH,
        WatchPosition.P12H,
        WatchPosition.P3H,
        WatchPosition.CB,
        WatchPosition.P6H,
        WatchPosition.P9H,
    };

    public static WatchHealthRadarModel Build(IReadOnlyList<PositionSummary> positions, RadarMetric metric)
    {
        var byPosition = new Dictionary<WatchPosition, PositionSummary>();
        foreach (PositionSummary position in positions)
        {
            byPosition[position.Position] = position;
        }

        MetricSpec spec = MetricSpec.For(metric);

        // Pass 1: size the radial scale to cover the measured values.
        double dataMin = double.PositiveInfinity;
        double dataMax = double.NegativeInfinity;
        foreach (WatchPosition position in AxisOrder)
        {
            if (byPosition.TryGetValue(position, out PositionSummary? summary) && spec.Stats(summary).Valid)
            {
                double radial = spec.RadiusValue(spec.Stats(summary).Mean);
                dataMin = Math.Min(dataMin, radial);
                dataMax = Math.Max(dataMax, radial);
            }
        }

        (double scaleMin, double scaleMax) = spec.Scale(dataMin, dataMax);
        double span = Math.Max(1e-9, scaleMax - scaleMin);
        double Fraction(double radial) => Math.Clamp((radial - scaleMin) / span, 0.0, 1.0);

        // Pass 2: build the axes and the running diagnosis.
        var axes = new List<RadarAxis>(AxisOrder.Count);
        int measured = 0;
        double valueSum = 0.0;
        double valueMin = double.PositiveInfinity;
        double valueMax = double.NegativeInfinity;
        double weakestScore = double.PositiveInfinity;
        WatchPosition? weakest = null;
        var worst = VarioVerdictLevel.Pending;
        bool anyJudged = false;

        foreach (WatchPosition position in AxisOrder)
        {
            if (byPosition.TryGetValue(position, out PositionSummary? summary) && spec.Stats(summary).Valid)
            {
                StatsSummary stats = spec.Stats(summary);
                long beats = Math.Max(summary.Rate.Count, Math.Max(summary.Amplitude.Count, summary.BeatError.Count));
                axes.Add(new RadarAxis(
                    position,
                    position.ShortName(),
                    HasValue: true,
                    stats.Mean,
                    spec.Format(stats.Mean),
                    Fraction(spec.RadiusValue(stats.Mean)),
                    beats));

                measured++;
                valueSum += stats.Mean;
                valueMin = Math.Min(valueMin, stats.Mean);
                valueMax = Math.Max(valueMax, stats.Mean);

                double score = spec.HealthScore(stats.Mean);
                if (score < weakestScore)
                {
                    weakestScore = score;
                    weakest = position;
                }

                VarioVerdict verdict = spec.Verdict(stats);
                if (verdict.Level != VarioVerdictLevel.Pending)
                {
                    anyJudged = true;
                    if ((int)verdict.Level > (int)worst)
                    {
                        worst = verdict.Level;
                    }
                }
            }
            else
            {
                axes.Add(new RadarAxis(position, position.ShortName(), HasValue: false, 0.0, "—", 0.0, 0));
            }
        }

        (bool hasBand, double bandInner, double bandOuter) = spec.Band(scaleMin, span);

        var ticks = new List<string>(3);
        for (int i = 1; i <= 3; i++)
        {
            ticks.Add(spec.FormatScale(scaleMin + (i / 3.0) * span));
        }

        string summaryLine;
        string verdictText;
        VarioVerdictLevel level;
        if (measured == 0)
        {
            summaryLine = "Measure positions to build the radar.";
            verdictText = "Measuring…";
            level = VarioVerdictLevel.Pending;
        }
        else
        {
            double mean = valueSum / measured;
            double spread = measured >= 2 ? valueMax - valueMin : 0.0;
            summaryLine = spec.Summary(mean, spread, measured, AxisOrder.Count);
            if (anyJudged)
            {
                level = worst;
                verdictText = level switch
                {
                    VarioVerdictLevel.Bad => "ALERT — service required",
                    VarioVerdictLevel.Warn => "WATCH — keep measuring",
                    _ => "OK — healthy",
                };
            }
            else
            {
                level = VarioVerdictLevel.Pending;
                verdictText = "Measuring…";
            }
        }

        return new WatchHealthRadarModel(
            metric,
            spec.Title,
            spec.BetterHint,
            axes,
            hasBand,
            bandInner,
            bandOuter,
            ticks,
            measured,
            measured > 0 ? weakest : null,
            summaryLine,
            verdictText,
            level);
    }

    /// <summary>
    /// Per-measure projection: how a <see cref="PositionSummary"/> turns into a
    /// radial value, scale, healthy band, formatting, and verdict. The band and
    /// verdict thresholds are read live from the shared accept-band single source,
    /// so a Settings edit moves the radar with the other displays.
    /// </summary>
    private sealed class MetricSpec
    {
        public required string Title { get; init; }
        public required string BetterHint { get; init; }
        public required Func<PositionSummary, StatsSummary> Stats { get; init; }
        public required Func<double, double> RadiusValue { get; init; }
        public required Func<double, double, (double Min, double Max)> Scale { get; init; }
        public required Func<double, double, (bool Has, double Inner, double Outer)> Band { get; init; }
        public required Func<double, string> Format { get; init; }
        public required Func<double, string> FormatScale { get; init; }
        public required Func<StatsSummary, VarioVerdict> Verdict { get; init; }
        public required Func<double, double> HealthScore { get; init; }
        public required Func<double, double, int, int, string> Summary { get; init; }

        private static (bool, double, double) RingBand(double min, double max, double scaleMin, double span)
        {
            double inner = Math.Clamp((min - scaleMin) / span, 0.0, 1.0);
            double outer = Math.Clamp((max - scaleMin) / span, 0.0, 1.0);
            return (outer > inner, inner, outer);
        }

        public static MetricSpec For(RadarMetric metric) => metric switch
        {
            RadarMetric.Rate => Rate(),
            RadarMetric.BeatError => BeatError(),
            _ => Amplitude(),
        };

        private static MetricSpec Amplitude()
        {
            return new MetricSpec
            {
                Title = "Amplitude by position",
                BetterHint = "larger is healthier",
                Stats = p => p.Amplitude,
                RadiusValue = mean => mean,
                // Fixed instrument scale: service-low (180) to over-banked (330).
                Scale = (dataMin, dataMax) => (
                    Math.Min(180.0, double.IsFinite(dataMin) ? dataMin : 180.0),
                    Math.Max(330.0, double.IsFinite(dataMax) ? dataMax : 330.0)),
                Band = (scaleMin, span) => RingBand(
                    VarioGaugePolicy.AmplitudeAcceptMinDeg, VarioGaugePolicy.AmplitudeAcceptMaxDeg, scaleMin, span),
                Format = v => string.Format(CultureInfo.InvariantCulture, "{0:0}°", v),
                FormatScale = v => string.Format(CultureInfo.InvariantCulture, "{0:0}°", v),
                Verdict = s => VarioVerdict.ForAmplitude(
                    s, VarioGaugePolicy.AmplitudeAcceptMinDeg, VarioGaugePolicy.AmplitudeAcceptMaxDeg),
                HealthScore = mean => mean,
                Summary = (mean, spread, measured, total) => string.Format(
                    CultureInfo.InvariantCulture, "Mean {0:0}° · spread {1:0}° · {2}/{3} positions",
                    mean, spread, measured, total),
            };
        }

        private static MetricSpec Rate()
        {
            double center = (VarioGaugePolicy.RateAcceptMinSPerDay + VarioGaugePolicy.RateAcceptMaxSPerDay) / 2.0;
            return new MetricSpec
            {
                Title = "Rate by position",
                BetterHint = "closer to the band is healthier",
                Stats = p => p.Rate,
                RadiusValue = mean => mean,
                Scale = (dataMin, dataMax) =>
                {
                    double lo = Math.Min(VarioGaugePolicy.RateAcceptMinSPerDay - 10.0, double.IsFinite(dataMin) ? dataMin - 2.0 : 0.0);
                    double hi = Math.Max(VarioGaugePolicy.RateAcceptMaxSPerDay + 10.0, double.IsFinite(dataMax) ? dataMax + 2.0 : 0.0);
                    return (lo, hi);
                },
                Band = (scaleMin, span) => RingBand(
                    VarioGaugePolicy.RateAcceptMinSPerDay, VarioGaugePolicy.RateAcceptMaxSPerDay, scaleMin, span),
                Format = v => string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0} s/d", v),
                FormatScale = v => string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}", v),
                Verdict = s => VarioVerdict.ForRate(
                    s, VarioGaugePolicy.RateAcceptMinSPerDay, VarioGaugePolicy.RateAcceptMaxSPerDay),
                HealthScore = mean => -Math.Abs(mean - center),
                Summary = (mean, spread, measured, total) => string.Format(
                    CultureInfo.InvariantCulture, "Mean {0:+0;-0;0} s/d · spread {1:0} s/d · {2}/{3} positions",
                    mean, spread, measured, total),
            };
        }

        private static MetricSpec BeatError()
        {
            return new MetricSpec
            {
                Title = "Beat error by position",
                BetterHint = "smaller is healthier",
                Stats = p => p.BeatError,
                RadiusValue = mean => Math.Abs(mean),
                Scale = (dataMin, dataMax) => (0.0, Math.Max(2.0, (double.IsFinite(dataMax) ? dataMax : 0.0) + 0.5)),
                // Healthy zone: within ±1 ms of perfect (the industry ≤1 ms convention).
                Band = (scaleMin, span) => RingBand(0.0, 1.0, scaleMin, span),
                Format = v => string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0} ms", v),
                FormatScale = v => string.Format(CultureInfo.InvariantCulture, "{0:0.0} ms", v),
                Verdict = BeatErrorVerdict,
                HealthScore = mean => -Math.Abs(mean),
                Summary = (mean, spread, measured, total) => string.Format(
                    CultureInfo.InvariantCulture, "Mean {0:+0.0;-0.0;0.0} ms · spread {1:0.0} ms · {2}/{3} positions",
                    mean, spread, measured, total),
            };
        }

        // Beat error has no shared VarioVerdict; gate on the same warm-up count and
        // grade the magnitude against the ±1 ms (good) / ±2 ms (watch) convention.
        private static VarioVerdict BeatErrorVerdict(StatsSummary stats)
        {
            if (!stats.Valid || stats.Count < VarioVerdict.MinSamples)
            {
                return VarioVerdict.Measuring;
            }

            double magnitude = Math.Abs(stats.Mean);
            if (magnitude <= 1.0)
            {
                return new VarioVerdict("Good", VarioVerdictLevel.Good);
            }

            return magnitude <= 2.0
                ? new VarioVerdict("Marginal", VarioVerdictLevel.Warn)
                : new VarioVerdict("High", VarioVerdictLevel.Bad);
        }
    }
}
