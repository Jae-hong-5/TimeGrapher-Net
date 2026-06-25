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
        // Project() runs on every frame, so re-bind ONLY when the key actually
        // changes: an unconditional Bind leaks a fresh resource-observable
        // subscription onto the same TextBlock on every frame the Health tab renders.
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
        _verdict.Bind(TextBlock.ForegroundProperty, _verdict.GetResourceObservable(key));
    }
}
