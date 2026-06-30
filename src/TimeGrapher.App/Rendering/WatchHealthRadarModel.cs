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
/// One row of the Health "Levels" list: a position's amplitude/rate/beat-error
/// readings and the worst severity among the three (the status dot). Formatted
/// text is carried so the view stays free of culture/formatting concerns.
/// </summary>
internal sealed record HealthLevelRow(
    WatchPosition Position,
    string Label,
    bool HasValue,
    string AmplitudeText,
    string RateText,
    string BeatErrorText,
    VarioVerdictLevel Level);

/// <summary>
/// One horizontal (flat) position's gauge row for the selected metric, mirroring
/// the Positions tab's <see cref="RateRangeLaneControl"/> visual language: a
/// measured min–max range plus the mean, all as 0..1 fractions on the shared radar
/// scale so the control only scales them to pixels. <see cref="OutOfBand"/> colours
/// the mean dot red when the mean falls outside the shared accept band.
/// </summary>
internal sealed record HealthHorizontalRow(
    WatchPosition Position,
    string Label,
    bool HasValue,
    double MeanFraction,
    double MinFraction,
    double MaxFraction,
    bool OutOfBand,
    string ValueText);

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
    IReadOnlyList<RadarAxis> Axes,
    bool HasBand,
    double BandInnerFraction,
    double BandOuterFraction,
    IReadOnlyList<string> ScaleTicks,
    int MeasuredCount,
    WatchPosition? WeakestPosition,
    string SummaryLine,
    string VerdictText,
    VarioVerdictLevel VerdictLevel,
    IReadOnlyList<HealthLevelRow> Levels,
    ConsistencyDiagnosis Consistency,
    string OverallText,
    VarioVerdictLevel OverallLevel,
    IReadOnlyList<HealthHorizontalRow> Horizontal)
{
    /// <summary>
    /// The eight vertical (hanging) positions, clockwise from the top, so each
    /// vertex angle matches the watch's actual clock orientation: 12H at the top,
    /// then the 45° intermediate positions interleaved with the cardinal ones
    /// (1:30 · 3 · 4:30 · 6 · 7:30 · 9 · 10:30). The radar polygon is therefore a
    /// regular octagon of the vertical positions only — the flat horizontal
    /// positions (CH/CB) are a different axis and are reported separately in
    /// <see cref="Horizontal"/>.
    /// </summary>
    public static readonly IReadOnlyList<WatchPosition> AxisOrder = new[]
    {
        WatchPosition.P12H,
        WatchPosition.P3H45,
        WatchPosition.P3H,
        WatchPosition.P6H45,
        WatchPosition.P6H,
        WatchPosition.P9H45,
        WatchPosition.P9H,
        WatchPosition.P12H45,
    };

    /// <summary>
    /// The two flat (horizontal) positions, reported beside the octagon rather than
    /// as radar vertices. They still feed the band-conformance verdict and the
    /// weakest-position pick (see <see cref="VerdictOrder"/>).
    /// </summary>
    public static readonly IReadOnlyList<WatchPosition> HorizontalOrder = new[]
    {
        WatchPosition.CH,
        WatchPosition.CB,
    };

    /// <summary>
    /// Every position the verdict and the radial scale span: the eight vertical
    /// radar axes plus the two horizontal positions. A flat-position fault (e.g. a
    /// dial-down service-low amplitude) must still drive the health verdict even
    /// though it is not a radar vertex, and the horizontal gauges share the radar's
    /// scale and accept band.
    /// </summary>
    public static readonly IReadOnlyList<WatchPosition> VerdictOrder = new[]
    {
        WatchPosition.P12H,
        WatchPosition.P3H45,
        WatchPosition.P3H,
        WatchPosition.P6H45,
        WatchPosition.P6H,
        WatchPosition.P9H45,
        WatchPosition.P9H,
        WatchPosition.P12H45,
        WatchPosition.CH,
        WatchPosition.CB,
    };

    public static WatchHealthRadarModel Build(
        IReadOnlyList<PositionSummary> positions, RadarMetric metric, WatchPosition activePosition = default)
    {
        var byPosition = new Dictionary<WatchPosition, PositionSummary>();
        foreach (PositionSummary position in positions)
        {
            byPosition[position.Position] = position;
        }

        MetricSpec spec = MetricSpec.For(metric);
        MetricSpec ampSpec = MetricSpec.For(RadarMetric.Amplitude);
        MetricSpec rateSpec = MetricSpec.For(RadarMetric.Rate);
        MetricSpec beatSpec = MetricSpec.For(RadarMetric.BeatError);

        // Pass 1: size the radial scale to cover every measured value — the vertical
        // radar axes AND the horizontal positions — so the horizontal CH/CB gauges
        // share the radar's scale and accept band.
        double dataMin = double.PositiveInfinity;
        double dataMax = double.NegativeInfinity;
        foreach (WatchPosition position in VerdictOrder)
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
        (bool hasBand, double bandInner, double bandOuter) = spec.Band(scaleMin, span);

        // Pass 2: build the vertical radar axes (the octagon polygon) and the
        // running mean/spread of the vertical readings for the summary line.
        var axes = new List<RadarAxis>(AxisOrder.Count);
        int measured = 0;
        double valueSum = 0.0;
        double valueMin = double.PositiveInfinity;
        double valueMax = double.NegativeInfinity;

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
            }
            else
            {
                axes.Add(new RadarAxis(position, position.ShortName(), HasValue: false, 0.0, "—", 0.0, 0));
            }
        }

        // Pass 3: the band-conformance verdict and weakest-position pick span EVERY
        // measured position (vertical AND horizontal): a flat-position fault must
        // still drive the health verdict even though it is not a radar vertex.
        double weakestScore = double.PositiveInfinity;
        WatchPosition? weakest = null;
        var worst = VarioVerdictLevel.Pending;
        bool anyJudged = false;
        foreach (WatchPosition position in VerdictOrder)
        {
            if (!byPosition.TryGetValue(position, out PositionSummary? summary) || !spec.Stats(summary).Valid)
            {
                continue;
            }

            StatsSummary stats = spec.Stats(summary);
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

        var ticks = new List<string>(3);
        for (int i = 1; i <= 3; i++)
        {
            ticks.Add(spec.FormatScale(scaleMin + (i / 3.0) * span));
        }

        string summaryLine;
        string verdictText;
        VarioVerdictLevel level;
        if (measured == 0 && !anyJudged)
        {
            summaryLine = "Measure positions to build the radar.";
            verdictText = "Measuring…";
            level = VarioVerdictLevel.Pending;
        }
        else
        {
            double mean = measured > 0 ? valueSum / measured : 0.0;
            double spread = measured >= 2 ? valueMax - valueMin : 0.0;
            summaryLine = spec.Summary(mean, spread, measured, AxisOrder.Count);
            if (anyJudged)
            {
                level = worst;
                verdictText = level switch
                {
                    VarioVerdictLevel.Bad => "ALERT — Review Required",
                    VarioVerdictLevel.Warn => "WATCH — Review",
                    _ => "OK — In Range",
                };
            }
            else
            {
                level = VarioVerdictLevel.Pending;
                verdictText = "Measuring…";
            }
        }

        // Levels axis: each position's three readings plus the worst severity
        // among them (the status dot), read from all three measures (not just the
        // selected radar metric).
        var levels = new List<HealthLevelRow>(AxisOrder.Count);
        foreach (WatchPosition position in AxisOrder)
        {
            if (byPosition.TryGetValue(position, out PositionSummary? summary) &&
                (summary.Amplitude.Valid || summary.Rate.Valid || summary.BeatError.Valid))
            {
                levels.Add(new HealthLevelRow(
                    position,
                    position.ShortName(),
                    HasValue: true,
                    summary.Amplitude.Valid ? ampSpec.Format(summary.Amplitude.Mean) : "—",
                    summary.Rate.Valid ? rateSpec.Format(summary.Rate.Mean) : "—",
                    summary.BeatError.Valid ? beatSpec.Format(summary.BeatError.Mean) : "—",
                    WorstLevel(
                        ampSpec.Verdict(summary.Amplitude),
                        rateSpec.Verdict(summary.Rate),
                        beatSpec.Verdict(summary.BeatError))));
            }
            else
            {
                levels.Add(new HealthLevelRow(
                    position, position.ShortName(), HasValue: false, "—", "—", "—", VarioVerdictLevel.Pending));
            }
        }

        // Horizontal axis: the flat positions (CH/CB) for the selected metric, as a
        // min–max range plus the mean on the shared radar scale (the Positions-lane
        // gauge language). Reported beside the octagon, not as radar vertices.
        var horizontal = new List<HealthHorizontalRow>(HorizontalOrder.Count);
        foreach (WatchPosition position in HorizontalOrder)
        {
            if (byPosition.TryGetValue(position, out PositionSummary? summary) && spec.Stats(summary).Valid)
            {
                StatsSummary stats = spec.Stats(summary);
                double meanFraction = Fraction(spec.RadiusValue(stats.Mean));
                // RadiusValue can be non-monotonic across zero (e.g. Math.Abs for beat
                // error), so when the SIGNED sample range straddles zero the magnitude
                // extreme is at zero, not at an endpoint. Take the radius at both ends
                // and (when the range crosses zero) at zero, then map to fractions; using
                // Abs on each endpoint independently would otherwise show a wrong near
                // end (and collapse a symmetric range to a point).
                double rMin = spec.RadiusValue(stats.Min);
                double rMax = spec.RadiusValue(stats.Max);
                double rLo = Math.Min(rMin, rMax);
                double rHi = Math.Max(rMin, rMax);
                if (stats.Min <= 0.0 && stats.Max >= 0.0)
                {
                    double rZero = spec.RadiusValue(0.0);
                    rLo = Math.Min(rLo, rZero);
                    rHi = Math.Max(rHi, rZero);
                }
                double f1 = Fraction(rLo);
                double f2 = Fraction(rHi);
                bool outOfBand = hasBand && (meanFraction < bandInner - 1e-6 || meanFraction > bandOuter + 1e-6);
                horizontal.Add(new HealthHorizontalRow(
                    position,
                    position.ShortName(),
                    HasValue: true,
                    meanFraction,
                    Math.Min(f1, f2),
                    Math.Max(f1, f2),
                    outOfBand,
                    spec.Format(stats.Mean)));
            }
            else
            {
                horizontal.Add(new HealthHorizontalRow(
                    position, position.ShortName(), HasValue: false, 0.0, 0.0, 0.0, false, "—"));
            }
        }

        // Consistency axis: reuse the Positions sequence judgment over the same snapshot.
        ConsistencyDiagnosis consistency =
            ConsistencyDiagnosis.Compute(SequenceSummary.Compute(positions), activePosition);

        // Overall = the worse severity of the two axes (band conformance, consistency).
        VarioVerdictLevel overallLevel = (VarioVerdictLevel)Math.Max((int)level, (int)consistency.Level);
        string overallText = overallLevel switch
        {
            VarioVerdictLevel.Bad => "ALERT — Review Required",
            VarioVerdictLevel.Warn => "WATCH — Review",
            VarioVerdictLevel.Good => "OK — In Range",
            _ => "Measuring…",
        };

        return new WatchHealthRadarModel(
            metric,
            axes,
            hasBand,
            bandInner,
            bandOuter,
            ticks,
            measured,
            weakest,
            summaryLine,
            verdictText,
            level,
            levels,
            consistency,
            overallText,
            overallLevel,
            horizontal);
    }

    /// <summary>Worst non-pending severity across a position's three measure verdicts.</summary>
    private static VarioVerdictLevel WorstLevel(params VarioVerdict[] verdicts)
    {
        var worst = VarioVerdictLevel.Pending;
        foreach (VarioVerdict verdict in verdicts)
        {
            if (verdict.Level != VarioVerdictLevel.Pending && (int)verdict.Level > (int)worst)
            {
                worst = verdict.Level;
            }
        }

        return worst;
    }

    /// <summary>
    /// Per-measure projection: how a <see cref="PositionSummary"/> turns into a
    /// radial value, scale, healthy band, formatting, and verdict. The band and
    /// verdict thresholds are read live from the shared accept-band single source,
    /// so a Settings edit moves the radar with the other displays.
    /// </summary>
    private sealed class MetricSpec
    {
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
                Stats = p => p.BeatError,
                RadiusValue = mean => Math.Abs(mean),
                Scale = (dataMin, dataMax) => (0.0, Math.Max(
                    AcceptBandSettings.Current.BeatErrorMagnitudeMs + 0.5,
                    Math.Max(2.0, (double.IsFinite(dataMax) ? dataMax : 0.0) + 0.5))),
                // Healthy zone: within the shared accept band (the same magnitude every
                // other beat-error display judges against), so a Settings edit re-projects
                // the radar consistently instead of a hardcoded ≤1 ms convention.
                Band = (scaleMin, span) => RingBand(
                    0.0, AcceptBandSettings.Current.BeatErrorMagnitudeMs, scaleMin, span),
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
        // grade the magnitude against the shared accept band: Good within the band,
        // Marginal up to 2x it, High beyond. Tracks the Settings value so the radar
        // verdict agrees with the other beat-error displays by construction.
        private static VarioVerdict BeatErrorVerdict(StatsSummary stats)
        {
            if (!stats.Valid || stats.Count < VarioVerdict.MinSamples)
            {
                return VarioVerdict.Measuring;
            }

            double band = AcceptBandSettings.Current.BeatErrorMagnitudeMs;
            double magnitude = Math.Abs(stats.Mean);
            if (magnitude <= band)
            {
                return new VarioVerdict("Good", VarioVerdictLevel.Good);
            }

            return magnitude <= 2.0 * band
                ? new VarioVerdict("Marginal", VarioVerdictLevel.Warn)
                : new VarioVerdict("High", VarioVerdictLevel.Bad);
        }
    }
}
