using System.Globalization;
using Avalonia.Controls;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Multi-Position Sequence Display: a TextBlock table of the per-position
/// results (POS | RATE | AMP | BEAT ERR | BEATS, one row per measured
/// position, the active position's row highlighted via the accent style
/// classes) above the X / D / vertical-vs-horizontal summary block, all
/// computed by <see cref="SequenceSummary"/> from the cumulative snapshot the
/// frame already carries. The accent banner reports the balance-wheel
/// unbalance hint. The table is rebuilt only when the snapshot version
/// changes (at most one rebuild per Core snapshot interval, bounded at the
/// six standard rows), so coalesced or repeated frames cost nothing.
/// </summary>
internal sealed class MultiPositionSeqRenderer
{
    private const string ActiveRowClass = "SeqActiveRow";
    private const string OnAccentClass = "SeqOnAccent";

    private static readonly string[] Headers = { "POS", "RATE", "AMP", "BEAT ERR", "BEATS" };

    private readonly Grid _tableGrid;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;
    private readonly TextBlock _summaryText;

    private ulong _lastVersion;

    public MultiPositionSeqRenderer(
        Grid tableGrid,
        Border alertBanner,
        TextBlock alertText,
        TextBlock summaryText)
    {
        _tableGrid = tableGrid;
        _alertBanner = alertBanner;
        _alertText = alertText;
        _summaryText = summaryText;
        Reset();
    }

    public void Reset()
    {
        _lastVersion = 0;
        _alertBanner.IsVisible = false;
        SequenceSummary empty = SequenceSummary.Compute(Array.Empty<PositionSummary>());
        RebuildTable(empty.Rows, activePosition: null);
        _summaryText.Text = FormatSummary(empty);
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
        _summaryText.Text = FormatSummary(summary);
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

        for (int index = 0; index < rows.Count; index++)
        {
            SequencePositionRow row = rows[index];
            int gridRow = index + 1;
            _tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            bool active = row.Position == activePosition;
            if (active)
            {
                // Accent highlight behind the row currently being measured
                // (the TestPositions "active" styling, declared in App.axaml
                // so it re-themes via DynamicResource).
                var highlight = new Border { Classes = { ActiveRowClass } };
                Grid.SetRow(highlight, gridRow);
                Grid.SetColumnSpan(highlight, Headers.Length);
                _tableGrid.Children.Add(highlight);
            }

            AddCell(row.Position.ShortName(), gridRow, 0, isHeader: false, active);
            AddCell(VarioReadout.Format(row.RateSPerDay, "+0.0;-0.0;0.0", " s/d"), gridRow, 1, isHeader: false, active);
            AddCell(VarioReadout.Format(row.AmplitudeDeg, "0", "°"), gridRow, 2, isHeader: false, active);
            AddCell(VarioReadout.Format(row.BeatErrorMs, "+0.00;-0.00;0.00", " ms"), gridRow, 3, isHeader: false, active);
            AddCell(row.Beats.ToString(CultureInfo.InvariantCulture), gridRow, 4, isHeader: false, active);
        }
    }

    private void AddCell(string text, int row, int column, bool isHeader, bool onAccent)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = isHeader ? 11 : 14,
            Opacity = isHeader ? 0.65 : 1.0,
            Margin = new Avalonia.Thickness(8, 3),
        };
        if (onAccent)
        {
            cell.Classes.Add(OnAccentClass);
        }

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        _tableGrid.Children.Add(cell);
    }

    /// <summary>Witschi-style sequence measures: X / D rows plus the V/H comparison (DVH).</summary>
    private static string FormatSummary(SequenceSummary summary) =>
        "X    RATE " + VarioReadout.Format(summary.RateMeanSPerDay, "+0.0;-0.0;0.0", " s/d")
        + "   AMP " + VarioReadout.Format(summary.AmplitudeMeanDeg, "0", "°")
        + "\nD    RATE " + VarioReadout.Format(summary.RateSpreadSPerDay, "0.0", " s/d")
        + "   AMP " + VarioReadout.Format(summary.AmplitudeSpreadDeg, "0", "°")
        + "\nV/H  VERT " + VarioReadout.Format(summary.VerticalRateMeanSPerDay, "+0.0;-0.0;0.0", " s/d")
        + "   HORIZ " + VarioReadout.Format(summary.HorizontalRateMeanSPerDay, "+0.0;-0.0;0.0", " s/d")
        + "   DVH " + VarioReadout.Format(summary.VerticalHorizontalRateDeltaSPerDay, "+0.0;-0.0;0.0", " s/d");

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
