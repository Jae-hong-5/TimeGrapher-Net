using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Renders the Positions tab's merged per-position table: the measured means
/// (rate / amplitude / beat error / beats), a rate-range acquisition lane
/// (<see cref="RateRangeLaneControl"/>, min–mean–max vs the accept band) and a
/// collection-progress cell, one row per position. Cross-position consistency is
/// judged on the Health tab now, so this is a pure data view built from the
/// per-position <see cref="PositionSummary"/> stats the frame snapshot carries —
/// no new analysis data. Red is reserved for out-of-accept-band values.
/// </summary>
internal sealed class MultiPositionSeqRenderer
{
    private const string ActiveRowClass = "SeqActiveRow";

    private static readonly string[] Headers =
        { "Position", "Error Rate", "Amplitude", "Beat Error", "Beats", "Error Rate vs Band", "Collection" };

    private readonly Grid _tableGrid;
    private readonly PositionSequenceDashboardControls _dashboard;
    private readonly WatchPosition _initialPosition;

    private ulong _lastVersion;
    private WatchPosition _activePosition;
    private IReadOnlyList<PositionSummary> _lastPositions = Array.Empty<PositionSummary>();

    public MultiPositionSeqRenderer(
        Grid tableGrid,
        PositionSequenceDashboardControls dashboard,
        WatchPosition initialPosition)
    {
        _tableGrid = tableGrid;
        _dashboard = dashboard;
        _initialPosition = initialPosition;
        _activePosition = initialPosition;
        Reset();
    }

    public void Reset() => Reset(_initialPosition);

    public void Reset(WatchPosition activePosition)
    {
        _lastVersion = 0;
        _activePosition = activePosition;
        _lastPositions = Array.Empty<PositionSummary>();
        RebuildTable(_lastPositions, activePosition);
        UpdateHero(_lastPositions, activePosition);
    }

    public void RequestPosition(WatchPosition position)
    {
        _activePosition = position;
        RebuildTable(_lastPositions, position);
        UpdateHero(_lastPositions, position);
    }

    public void ApplyAcceptBands()
    {
        RebuildTable(_lastPositions, _activePosition);
        UpdateHero(_lastPositions, _activePosition);
    }

    public void RenderFrame(AnalysisFrame frame)
    {
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        if (history == null || history.Version == _lastVersion)
        {
            return;
        }

        _lastVersion = history.Version;
        _activePosition = history.ActivePosition;
        _lastPositions = history.Positions ?? Array.Empty<PositionSummary>();
        RebuildTable(_lastPositions, history.ActivePosition);
        UpdateHero(_lastPositions, history.ActivePosition);
    }

    private void UpdateHero(IReadOnlyList<PositionSummary> positions, WatchPosition active)
    {
        var byPosition = new Dictionary<WatchPosition, PositionSummary>();
        foreach (PositionSummary position in positions)
        {
            byPosition[position.Position] = position;
        }

        byPosition.TryGetValue(active, out PositionSummary? summary);
        StatsSummary rate = summary?.Rate ?? default;
        StatsSummary amp = summary?.Amplitude ?? default;
        StatsSummary beat = summary?.BeatError ?? default;
        long beats = summary is null ? 0 : Math.Max(rate.Count, Math.Max(amp.Count, beat.Count));

        _dashboard.LiveRate.Text = VarioReadout.Format(rate.Valid ? rate.Mean : (double?)null, "+0.0;-0.0;0.0", " s/d");
        _dashboard.LiveAmplitude.Text = VarioReadout.Format(amp.Valid ? amp.Mean : (double?)null, "0", "°");
        _dashboard.LiveBeatError.Text = VarioReadout.Format(beat.Valid ? beat.Mean : (double?)null, "+0.00;-0.00; 0.00", " ms");
        _dashboard.LiveBeats.Text = beats.ToString(CultureInfo.InvariantCulture);

        long threshold = VarioVerdict.MinSamples;
        bool measured = beats > 0;
        bool qualified = beats >= threshold;
        double fraction = threshold <= 0 ? 0.0 : Math.Clamp(beats / (double)threshold, 0.0, 1.0);
        _dashboard.CollectionBar.ColumnDefinitions = new ColumnDefinitions(string.Format(
            CultureInfo.InvariantCulture, "{0:0.###}*,{1:0.###}*", fraction, 1.0 - fraction));
        _dashboard.CollectionFill.IsVisible = measured;
        if (measured)
        {
            _dashboard.CollectionFill.Bind(Border.BackgroundProperty,
                _dashboard.CollectionFill.GetResourceObservable(qualified ? "VarioGoodBrush" : "VarioWarnBrush"));
        }

        _dashboard.CollectionLabel.Text = measured ? $"{beats} / {threshold} beats" : "not measured";

        SequenceSummary sequence = SequenceSummary.Compute(positions);
        _dashboard.SeqRate.Text = VarioReadout.Format(sequence.RateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.SeqAmplitude.Text = VarioReadout.Format(sequence.AmplitudeMeanDeg, "0", "°");

        int measuredCount = 0;
        long totalBeats = 0;
        foreach (PositionSummary position in positions)
        {
            long b = Math.Max(position.Rate.Count, Math.Max(position.Amplitude.Count, position.BeatError.Count));
            if (b > 0)
            {
                measuredCount++;
            }

            totalBeats += b;
        }

        _dashboard.PositionsMeasured.Text = $"{measuredCount} / {WatchPositions.All.Count}";
        _dashboard.TotalBeats.Text = totalBeats.ToString(CultureInfo.InvariantCulture);
    }

    private void RebuildTable(IReadOnlyList<PositionSummary> positions, WatchPosition? activePosition)
    {
        _tableGrid.Children.Clear();
        _tableGrid.RowDefinitions.Clear();

        _tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int column = 0; column < Headers.Length; column++)
        {
            AddHeader(Headers[column], column);
        }

        var byPosition = new Dictionary<WatchPosition, PositionSummary>();
        foreach (PositionSummary position in positions)
        {
            byPosition[position.Position] = position;
        }

        IReadOnlyList<WatchPosition> all = WatchPositions.All;
        for (int index = 0; index < all.Count; index++)
        {
            WatchPosition position = all[index];
            int gridRow = index + 1;
            _tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            if (position == activePosition)
            {
                var highlight = new Border { Classes = { ActiveRowClass } };
                Grid.SetRow(highlight, gridRow);
                Grid.SetColumnSpan(highlight, Headers.Length);
                _tableGrid.Children.Add(highlight);
            }

            byPosition.TryGetValue(position, out PositionSummary? summary);
            StatsSummary rate = summary?.Rate ?? default;
            StatsSummary amp = summary?.Amplitude ?? default;
            StatsSummary beat = summary?.BeatError ?? default;
            long beats = summary is null ? 0 : Math.Max(rate.Count, Math.Max(amp.Count, beat.Count));

            bool rateOut = rate.Valid &&
                (rate.Mean < VarioGaugePolicy.RateAcceptMinSPerDay || rate.Mean > VarioGaugePolicy.RateAcceptMaxSPerDay);
            bool ampOut = amp.Valid &&
                (amp.Mean < VarioGaugePolicy.AmplitudeAcceptMinDeg || amp.Mean > VarioGaugePolicy.AmplitudeAcceptMaxDeg);
            bool beatOut = beat.Valid &&
                Math.Abs(beat.Mean) > AcceptBandSettings.Current.BeatErrorMagnitudeMs;

            AddText(position.ShortName(), gridRow, 0, bold: true, outOfBand: false);
            AddText(VarioReadout.Format(rate.Valid ? rate.Mean : (double?)null, "+0.0;-0.0;0.0", " s/d"), gridRow, 1, false, rateOut);
            AddText(VarioReadout.Format(amp.Valid ? amp.Mean : (double?)null, "0", "°"), gridRow, 2, false, ampOut);
            AddText(VarioReadout.Format(beat.Valid ? beat.Mean : (double?)null, "+0.00;-0.00; 0.00", " ms"), gridRow, 3, false, beatOut);
            AddText(beats > 0 ? beats.ToString(CultureInfo.InvariantCulture) : VarioReadout.Missing, gridRow, 4, false, false);

            var lane = new RateRangeLaneControl(
                rate.Valid, rate.Valid ? rate.Min : 0.0, rate.Mean, rate.Valid ? rate.Max : 0.0);
            Grid.SetRow(lane, gridRow);
            Grid.SetColumn(lane, 5);
            _tableGrid.Children.Add(lane);

            Control collection = BuildCollectionCell(beats);
            Grid.SetRow(collection, gridRow);
            Grid.SetColumn(collection, 6);
            _tableGrid.Children.Add(collection);
        }
    }

    private void AddHeader(string text, int column)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Opacity = 0.55,
            Margin = new Thickness(8, 0, 8, 4),
        };
        Grid.SetRow(cell, 0);
        Grid.SetColumn(cell, column);
        _tableGrid.Children.Add(cell);
    }

    private void AddText(string text, int row, int column, bool bold, bool outOfBand)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
        };
        if (outOfBand)
        {
            cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable("VarioBadBrush"));
        }

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        _tableGrid.Children.Add(cell);
    }

    private static Control BuildCollectionCell(long beats)
    {
        long threshold = VarioVerdict.MinSamples;
        bool measured = beats > 0;
        bool qualified = beats >= threshold;
        double fraction = threshold <= 0 ? 0.0 : Math.Clamp(beats / (double)threshold, 0.0, 1.0);

        var fill = new Border { CornerRadius = new CornerRadius(4) };
        if (measured)
        {
            fill.Bind(Border.BackgroundProperty,
                fill.GetResourceObservable(qualified ? "VarioGoodBrush" : "VarioWarnBrush"));
        }

        var bar = new Grid
        {
            Height = 8,
            ColumnDefinitions = new ColumnDefinitions(string.Format(
                CultureInfo.InvariantCulture, "{0:0.###}*,{1:0.###}*", fraction, 1.0 - fraction)),
        };
        Grid.SetColumn(fill, 0);
        bar.Children.Add(fill);

        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Child = bar };
        track.Bind(Border.BackgroundProperty, track.GetResourceObservable("ChromeBorderBrush"));

        var label = new TextBlock
        {
            Text = !measured ? "not measured" : qualified ? $"{threshold}+ beats" : $"{beats} / {threshold} beats",
            FontSize = 14,
            Opacity = 0.7,
            Margin = new Thickness(0, 3, 0, 0),
        };

        return new StackPanel
        {
            Margin = new Thickness(8, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { track, label },
        };
    }
}
