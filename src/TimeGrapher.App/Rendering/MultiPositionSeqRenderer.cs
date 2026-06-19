using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class MultiPositionSeqRenderer
{
    private const string ActiveRowClass = "SeqActiveRow";
    private const string OnAccentClass = "SeqOnAccent";
    private const string PositionMapActiveClass = "active";
    private const string ResultOkClass = "ok";
    private const string ResultWarnClass = "warn";
    private const string ResultPendingClass = "pending";
    private const long MinPositionBeatsForVerdict = VarioVerdict.MinSamples;
    private const int MinQualifiedPositionsForVerdict = 3;
    private const int MinVerticalPositionsForBalanceWheelVerdict = 2;

    private static readonly string[] Headers = { "POS", "ERROR RATE", "Amplitude", "BEAT ERROR", "BEATS" };

    private readonly Grid _tableGrid;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;
    private readonly PositionSequenceDashboardControls _dashboard;
    private readonly WatchPosition _initialPosition;

    private ulong _lastVersion;

    public MultiPositionSeqRenderer(
        Grid tableGrid,
        Border alertBanner,
        TextBlock alertText,
        PositionSequenceDashboardControls dashboard,
        WatchPosition initialPosition)
    {
        _tableGrid = tableGrid;
        _alertBanner = alertBanner;
        _alertText = alertText;
        _dashboard = dashboard;
        _initialPosition = initialPosition;
        Reset();
    }

    public void Reset()
    {
        _lastVersion = 0;
        _alertBanner.IsVisible = false;
        SequenceSummary empty = SequenceSummary.Compute(Array.Empty<PositionSummary>());
        RebuildTable(empty.Rows, activePosition: null);
        UpdateDashboard(empty, _initialPosition);
    }

    public void RenderFrame(AnalysisFrame frame)
    {
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        if (history == null || history.Version == _lastVersion)
        {
            return;
        }

        _lastVersion = history.Version;
        SequenceSummary summary = SequenceSummary.Compute(history.Positions);
        RebuildTable(summary.Rows, history.ActivePosition);
        UpdateDashboard(summary, history.ActivePosition);
        UpdateBanner(summary);
    }

    private void RebuildTable(IReadOnlyList<SequencePositionRow> rows, WatchPosition? activePosition)
    {
        _tableGrid.Children.Clear();
        _tableGrid.RowDefinitions.Clear();

        _tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int column = 0; column < Headers.Length; column++)
        {
            AddCell(Headers[column], 0, column, isHeader: true, onAccent: false);
        }

        Dictionary<WatchPosition, SequencePositionRow> rowsByPosition =
            rows.ToDictionary(row => row.Position);
        IReadOnlyList<WatchPosition> positions = WatchPositions.All;
        for (int index = 0; index < positions.Count; index++)
        {
            WatchPosition position = positions[index];
            rowsByPosition.TryGetValue(position, out SequencePositionRow? row);
            int gridRow = index + 1;
            _tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            bool active = position == activePosition;
            if (active)
            {
                var highlight = new Border { Classes = { ActiveRowClass } };
                Grid.SetRow(highlight, gridRow);
                Grid.SetColumnSpan(highlight, Headers.Length);
                _tableGrid.Children.Add(highlight);
            }

            AddCell(position.ShortName(), gridRow, 0, isHeader: false, active);
            AddCell(VarioReadout.Format(row?.RateSPerDay, "+0.0;-0.0;0.0", " s/d"), gridRow, 1, isHeader: false, active);
            AddCell(VarioReadout.Format(row?.AmplitudeDeg, "0", "°"), gridRow, 2, isHeader: false, active);
            AddCell(VarioReadout.Format(row?.BeatErrorMs, "+0.00;-0.00; 0.00", " ms"), gridRow, 3, isHeader: false, active);
            AddCell(row?.Beats.ToString(CultureInfo.InvariantCulture) ?? VarioReadout.Missing, gridRow, 4, isHeader: false, active);
        }
    }

    private void AddCell(string text, int row, int column, bool isHeader, bool onAccent)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 14,
            Opacity = isHeader ? 0.65 : 1.0,
            Margin = new Avalonia.Thickness(8, 3),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Left,
        };
        if (onAccent)
        {
            cell.Classes.Add(OnAccentClass);
        }

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        _tableGrid.Children.Add(cell);
    }

    private void UpdateDashboard(SequenceSummary summary, WatchPosition activePosition)
    {
        _dashboard.ActivePositionText.Text = activePosition.ShortName();
        _dashboard.ActiveOrientationText.Text = activePosition.LongName();

        foreach (PositionMapTileControls tile in _dashboard.PositionMapTiles)
        {
            bool active = tile.Position == activePosition;
            if (active && !tile.Tile.Classes.Contains(PositionMapActiveClass))
            {
                tile.Tile.Classes.Add(PositionMapActiveClass);
            }
            else if (!active)
            {
                tile.Tile.Classes.Remove(PositionMapActiveClass);
            }
        }

        UpdateConsistencyVerdict(summary, activePosition);
        _dashboard.AverageRateText.Text = VarioReadout.Format(summary.RateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.AverageAmplitudeText.Text = VarioReadout.Format(summary.AmplitudeMeanDeg, "0", "°");
        _dashboard.SpreadRateText.Text = VarioReadout.Format(summary.RateSpreadSPerDay, "0.0", " s/d");
        _dashboard.SpreadAmplitudeText.Text = VarioReadout.Format(summary.AmplitudeSpreadDeg, "0", "°");
        _dashboard.VerticalRateText.Text = VarioReadout.Format(summary.VerticalRateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.HorizontalRateText.Text = VarioReadout.Format(summary.HorizontalRateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.VerticalHorizontalDeltaText.Text = VarioReadout.Format(
            summary.VerticalHorizontalRateDeltaSPerDay,
            "+0.0;-0.0;0.0",
            " s/d");
    }

    private void UpdateConsistencyVerdict(SequenceSummary summary, WatchPosition activePosition)
    {
        _dashboard.ConsistencyBadge.Classes.Remove(ResultOkClass);
        _dashboard.ConsistencyBadge.Classes.Remove(ResultWarnClass);
        _dashboard.ConsistencyBadge.Classes.Remove(ResultPendingClass);

        SequencePositionRow[] qualifiedRows = summary.Rows
            .Where(row => row.RateSPerDay != null && row.Beats >= MinPositionBeatsForVerdict)
            .ToArray();
        _dashboard.ConsistencyGuideText.Text = FormatRequirementGuide(qualifiedRows);

        SequencePositionRow? activeRow = summary.Rows.FirstOrDefault(row => row.Position == activePosition);
        long activeBeats = activeRow?.Beats ?? 0;
        if (activeRow?.RateSPerDay == null || activeBeats < MinPositionBeatsForVerdict)
        {
            _dashboard.ConsistencyVerdictText.Text = "COLLECTING";
            _dashboard.ConsistencyDetailText.Text =
                $"{activePosition.ShortName()}: {activeBeats}/{MinPositionBeatsForVerdict} beats. Keep measuring this position.";
            _dashboard.ConsistencyBadge.Classes.Add(ResultPendingClass);
            return;
        }

        if (qualifiedRows.Length < MinQualifiedPositionsForVerdict)
        {
            _dashboard.ConsistencyVerdictText.Text = "COLLECTING";
            _dashboard.ConsistencyDetailText.Text =
                $"{qualifiedRows.Length}/{MinQualifiedPositionsForVerdict} positions ready. Measure another position to {MinPositionBeatsForVerdict} beats.";
            _dashboard.ConsistencyBadge.Classes.Add(ResultPendingClass);
            return;
        }

        // The guide advertises the balance-wheel requirement (vertical positions
        // and a 1V+1H spread), so the verdict must actually enforce it before
        // reporting OK/CHECK — otherwise an all-horizontal qualified set reads "OK"
        // while the guide still shows the vertical requirement unmet.
        int qualifiedVertical = qualifiedRows.Count(row =>
            !row.Position.IsHorizontal() && !row.Position.IsIntermediate());
        int qualifiedHorizontal = qualifiedRows.Count(row => row.Position.IsHorizontal());
        if (qualifiedVertical < MinVerticalPositionsForBalanceWheelVerdict || qualifiedHorizontal < 1)
        {
            _dashboard.ConsistencyVerdictText.Text = "COLLECTING";
            _dashboard.ConsistencyDetailText.Text =
                $"Need {MinVerticalPositionsForBalanceWheelVerdict} vertical and 1 horizontal position; have {qualifiedVertical}V/{qualifiedHorizontal}H qualified.";
            _dashboard.ConsistencyBadge.Classes.Add(ResultPendingClass);
            return;
        }

        double? qualifiedRateSpread = Spread(qualifiedRows.Select(row => row.RateSPerDay!.Value));
        double? qualifiedVerticalSpread = Spread(qualifiedRows
            .Where(row => !row.Position.IsHorizontal() && !row.Position.IsIntermediate())
            .Select(row => row.RateSPerDay!.Value));
        if (qualifiedRateSpread > SequenceSummary.UnbalanceVerticalRateSpreadSPerDay ||
            qualifiedVerticalSpread > SequenceSummary.UnbalanceVerticalRateSpreadSPerDay)
        {
            _dashboard.ConsistencyVerdictText.Text = "CHECK";
            _dashboard.ConsistencyDetailText.Text =
                $"{qualifiedRows.Length} positions ready. Spread is above {SequenceSummary.UnbalanceVerticalRateSpreadSPerDay:0} s/d.";
            _dashboard.ConsistencyBadge.Classes.Add(ResultWarnClass);
            return;
        }

        _dashboard.ConsistencyVerdictText.Text = "OK";
        _dashboard.ConsistencyDetailText.Text =
            $"{qualifiedRows.Length} positions ready. Spread is within {SequenceSummary.UnbalanceVerticalRateSpreadSPerDay:0} s/d.";
        _dashboard.ConsistencyBadge.Classes.Add(ResultOkClass);
    }

    private static string FormatRequirementGuide(SequencePositionRow[] qualifiedRows)
    {
        int verticalPositions = qualifiedRows.Count(row =>
            !row.Position.IsHorizontal() && !row.Position.IsIntermediate());
        int horizontalPositions = qualifiedRows.Count(row => row.Position.IsHorizontal());
        return $"Required: D {qualifiedRows.Length}/{MinQualifiedPositionsForVerdict} positions · Balance-wheel {verticalPositions}/{MinVerticalPositionsForBalanceWheelVerdict} vertical · V/H {verticalPositions}V+{horizontalPositions}H (need 1V+1H)";
    }

    private static double? Spread(IEnumerable<double> values)
    {
        double[] snapshot = values.ToArray();
        if (snapshot.Length < 2)
        {
            return null;
        }

        return snapshot.Max() - snapshot.Min();
    }

    private void UpdateBanner(SequenceSummary summary)
    {
        _alertBanner.IsVisible = summary.UnbalanceSuspected;
        if (summary.UnbalanceSuspected)
        {
            _alertText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "⚠ Possible balance-wheel unbalance: rate varies {0:0.0} s/d across the hanging positions (> {1:0} s/d).",
                summary.VerticalRateSpreadSPerDay,
                SequenceSummary.UnbalanceVerticalRateSpreadSPerDay);
        }
    }
}
