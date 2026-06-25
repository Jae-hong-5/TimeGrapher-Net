using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Drives the Watch Health radar: builds a <see cref="WatchHealthRadarModel"/> from
/// the per-position aggregates the frame snapshot carries and pushes it to the
/// control plus the diagnosis text. The last snapshot is retained so a metric
/// toggle or an accept-band edit re-projects the same data without waiting for a
/// new frame (the accept-band consumer contract). Kept out of the control so the
/// projection stays unit-testable through <see cref="WatchHealthRadarModel"/>.
/// </summary>
internal sealed class WatchHealthRadarRenderer
{
    private readonly WatchHealthRadarControl _radar;
    private readonly TextBlock _metricHint;
    private readonly TextBlock _verdict;
    private readonly TextBlock _summary;
    private readonly TextBlock _weakest;

    private RadarMetric _metric = RadarMetric.Amplitude;
    private IReadOnlyList<PositionSummary> _positions = Array.Empty<PositionSummary>();
    private string? _verdictBrushKey;
    private IDisposable? _verdictBrushBinding;

    public WatchHealthRadarRenderer(
        WatchHealthRadarControl radar,
        TextBlock metricHint,
        TextBlock verdict,
        TextBlock summary,
        TextBlock weakest)
    {
        _radar = radar;
        _metricHint = metricHint;
        _verdict = verdict;
        _summary = summary;
        _weakest = weakest;
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
        Project();
    }

    /// <summary>Re-read the accept band into the healthy ring and verdict (no run reset).</summary>
    public void ApplyAcceptBands() => Project();

    private void Project()
    {
        WatchHealthRadarModel model = WatchHealthRadarModel.Build(_positions, _metric);
        _radar.SetModel(model);

        _metricHint.Text = $"{model.MetricTitle} · {model.BetterHint}";
        _verdict.Text = model.VerdictText;
        ApplyVerdictColor(model.VerdictLevel);
        _summary.Text = model.SummaryLine;
        _weakest.Text = model.WeakestPosition is { } position
            ? $"Weakest: {position.ShortName()} · {position.LongName()}"
            : "Weakest: —";
    }

    private void ApplyVerdictColor(VarioVerdictLevel level)
    {
        // Bind to the shared theme brush so the verdict recolors with the theme.
        // Project() runs on every frame, so re-bind ONLY when the key actually changes,
        // and dispose the previous binding before installing the new one: Bind returns an
        // IDisposable that, if discarded, leaves the old resource-observable subscription
        // live on the TextBlock, so a verdict oscillating across Good/Warn/Bad thresholds
        // would otherwise accumulate subscriptions over the run. The same-key guard keeps
        // a steady verdict from rebinding at all (theme changes flow through the live one).
        string key = level switch
        {
            VarioVerdictLevel.Good => "VarioGoodBrush",
            VarioVerdictLevel.Warn => "VarioWarnBrush",
            VarioVerdictLevel.Bad => "VarioBadBrush",
            _ => "TextPrimaryBrush",
        };
        if (key == _verdictBrushKey)
        {
            return;
        }

        _verdictBrushKey = key;
        _verdictBrushBinding?.Dispose();
        _verdictBrushBinding = _verdict.Bind(TextBlock.ForegroundProperty, _verdict.GetResourceObservable(key));
    }
}
