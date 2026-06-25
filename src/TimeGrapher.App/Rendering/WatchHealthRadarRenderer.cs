using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>One Levels-list row's controls (a position's three readings + status dot).</summary>
internal sealed record HealthLevelRowControls(
    TextBlock Pos, TextBlock Amp, TextBlock Rate, TextBlock Beat, Border Dot);

/// <summary>One Consistency card's live controls (reading + status chip).</summary>
internal sealed record HealthConsistencyRowControls(TextBlock Reading, TextBlock Chip);

/// <summary>The mutable controls of the unified Diagnosis rail.</summary>
internal sealed record HealthDiagnosisControls(
    TextBlock Hint,
    TextBlock Overall,
    TextBlock OverallSub,
    IReadOnlyList<HealthLevelRowControls> LevelRows,
    TextBlock Weakest,
    HealthConsistencyRowControls Spread,
    HealthConsistencyRowControls Balance,
    HealthConsistencyRowControls VerticalHorizontal);

/// <summary>
/// Drives the Watch Health radar and its unified Diagnosis rail: builds a
/// <see cref="WatchHealthRadarModel"/> from the per-position aggregates the frame
/// snapshot carries and pushes it to the radar plus the rail (Overall verdict,
/// the per-position Levels list, and the cross-position Consistency cards). The
/// last snapshot is retained so a metric toggle or an accept-band edit re-projects
/// the same data without waiting for a new frame. Kept out of the controls so the
/// projection stays unit-testable through <see cref="WatchHealthRadarModel"/>.
/// </summary>
internal sealed class WatchHealthRadarRenderer
{
    private readonly WatchHealthRadarControl _radar;
    private readonly HealthDiagnosisControls _rail;

    private RadarMetric _metric = RadarMetric.Amplitude;
    private IReadOnlyList<PositionSummary> _positions = Array.Empty<PositionSummary>();
    private readonly Dictionary<TextBlock, (string Key, IDisposable Binding)> _foregroundBindings = new();
    private readonly Dictionary<Border, (string Key, IDisposable Binding)> _backgroundBindings = new();
    private WatchPosition _activePosition;

    public WatchHealthRadarRenderer(WatchHealthRadarControl radar, HealthDiagnosisControls rail)
    {
        _radar = radar;
        _rail = rail;
        Project();
    }

    public RadarMetric Metric => _metric;

    public void SetMetric(RadarMetric metric)
    {
        if (_metric == metric)
        {
            return;
        }

        _metric = metric;
        Project();
    }

    public void Reset()
    {
        _positions = Array.Empty<PositionSummary>();
        Project();
    }

    public void RenderFrame(AnalysisFrame frame)
    {
        _positions = frame.MetricsHistory?.Positions ?? Array.Empty<PositionSummary>();
        _activePosition = frame.MetricsHistory?.ActivePosition ?? _activePosition;
        Project();
    }

    /// <summary>Re-read the accept band into the healthy ring and verdict (no run reset).</summary>
    public void ApplyAcceptBands() => Project();

    private void Project()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(_positions, _metric, _activePosition);
        _radar.SetModel(model);

        _rail.Hint.Text = $"{model.MetricTitle} · {model.BetterHint}";

        _rail.Overall.Text = model.OverallText;
        BindForeground(_rail.Overall, LevelBrush(model.OverallLevel));
        _rail.OverallSub.Text = string.Format(
            CultureInfo.InvariantCulture,
            "worse of the two axes · {0}/{1} positions",
            model.MeasuredCount, WatchHealthRadarModel.AxisOrder.Count);

        ProjectLevels(model);

        _rail.Weakest.Text = model.WeakestPosition is { } position
            ? $"Weakest: {position.ShortName()} · {position.LongName()}"
            : "Weakest: —";

        ConsistencyDiagnosis consistency = model.Consistency;
        ProjectConsistency(_rail.Spread, FormatSpread(consistency.RateSpreadSPerDay), consistency.SpreadStatus);
        ProjectConsistency(_rail.Balance, FormatSpread(consistency.VerticalRateSpreadSPerDay), consistency.BalanceStatus);
        ProjectConsistency(_rail.VerticalHorizontal, FormatDelta(consistency.VerticalHorizontalRateDeltaSPerDay), consistency.VerticalHorizontalStatus);
    }

    private void ProjectLevels(WatchHealthRadarModel model)
    {
        // Emphasise the selected metric's column; dim the other two so the toggle
        // reads as "selected = primary, others = context".
        for (int i = 0; i < _rail.LevelRows.Count && i < model.Levels.Count; i++)
        {
            HealthLevelRowControls row = _rail.LevelRows[i];
            HealthLevelRow data = model.Levels[i];

            row.Pos.Text = data.Label;
            row.Amp.Text = data.AmplitudeText;
            row.Rate.Text = data.RateText;
            row.Beat.Text = data.BeatErrorText;

            row.Amp.Opacity = _metric == RadarMetric.Amplitude ? 1.0 : 0.45;
            row.Rate.Opacity = _metric == RadarMetric.Rate ? 1.0 : 0.45;
            row.Beat.Opacity = _metric == RadarMetric.BeatError ? 1.0 : 0.45;

            bool showDot = data.HasValue && data.Level != VarioVerdictLevel.Pending;
            row.Dot.IsVisible = showDot;
            if (showDot)
            {
                BindBackground(row.Dot, LevelBrush(data.Level));
            }
        }
    }

    private void ProjectConsistency(HealthConsistencyRowControls controls, string reading, ConsistencyStatus status)
    {
        controls.Reading.Text = reading;
        controls.Chip.Text = StatusText(status);
        BindForeground(controls.Chip, StatusBrush(status));
    }

    private static string FormatSpread(double? value) =>
        value is double v ? string.Format(CultureInfo.InvariantCulture, "{0:0} s/d", v) : "—";

    private static string FormatDelta(double? value) =>
        value is double v ? string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0} s/d", v) : "—";

    private static string StatusText(ConsistencyStatus status) => status switch
    {
        ConsistencyStatus.Ok => "OK",
        ConsistencyStatus.Check => "CHECK",
        ConsistencyStatus.Ready => "READY",
        ConsistencyStatus.Reference => "REFERENCE",
        _ => "COLLECTING",
    };

    private static string StatusBrush(ConsistencyStatus status) => status switch
    {
        ConsistencyStatus.Ok => "VarioGoodBrush",
        ConsistencyStatus.Ready => "VarioGoodBrush",
        ConsistencyStatus.Check => "VarioWarnBrush",
        _ => "TextPrimaryBrush",
    };

    private static string LevelBrush(VarioVerdictLevel level) => level switch
    {
        VarioVerdictLevel.Good => "VarioGoodBrush",
        VarioVerdictLevel.Warn => "VarioWarnBrush",
        VarioVerdictLevel.Bad => "VarioBadBrush",
        _ => "TextPrimaryBrush",
    };

    // Bind to shared theme brushes so colours track the theme. Project() runs on
    // every frame, so replace a binding only when a control's resource key changes.
    private void BindForeground(TextBlock target, string key)
    {
        if (_foregroundBindings.TryGetValue(target, out (string Key, IDisposable Binding) current))
        {
            if (current.Key == key)
            {
                return;
            }

            current.Binding.Dispose();
        }

        _foregroundBindings[target] = (
            key,
            target.Bind(TextBlock.ForegroundProperty, target.GetResourceObservable(key)));
    }

    private void BindBackground(Border target, string key)
    {
        if (_backgroundBindings.TryGetValue(target, out (string Key, IDisposable Binding) current))
        {
            if (current.Key == key)
            {
                return;
            }

            current.Binding.Dispose();
        }

        _backgroundBindings[target] = (
            key,
            target.Bind(Border.BackgroundProperty, target.GetResourceObservable(key)));
    }
}
