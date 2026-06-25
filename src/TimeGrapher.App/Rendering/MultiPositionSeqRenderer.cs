using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Renders the Positions tab's per-position measurement table (the means each
/// position recorded) and tracks the active position. Cross-position consistency
/// is judged on the Health tab now, so this renderer is a pure data view: it
/// rebuilds the table from the cumulative <see cref="SequenceSummary"/> the frame
/// snapshot carries.
/// </summary>
internal sealed class MultiPositionSeqRenderer
{
    private const string ActiveRowClass = "SeqActiveRow";
    private const string OnAccentClass = "SeqOnAccent";

    private static readonly string[] Headers = { "POS", "Error Rate", "Amplitude", "BEAT ERROR", "BEATS" };

    private readonly Grid _tableGrid;
    private readonly PositionSequenceDashboardControls _dashboard;
    private readonly WatchPosition _initialPosition;

    private ulong _lastVersion;
    private SequenceSummary _lastSummary = SequenceSummary.Compute(Array.Empty<PositionSummary>());

    public MultiPositionSeqRenderer(
        Grid tableGrid,
        PositionSequenceDashboardControls dashboard,
        WatchPosition initialPosition)
    {
        _tableGrid = tableGrid;
        _dashboard = dashboard;
        _initialPosition = initialPosition;
        Reset();
    }

    public void Reset()
    {
        Reset(_initialPosition);
    }

    public void Reset(WatchPosition activePosition)
    {
        _lastVersion = 0;
        _lastSummary = SequenceSummary.Compute(Array.Empty<PositionSummary>());
        RebuildTable(_lastSummary.Rows, activePosition);
        SetActivePosition(activePosition);
    }

    public void RequestPosition(WatchPosition position)
    {
        RebuildTable(_lastSummary.Rows, position);
        SetActivePosition(position);
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
        _lastSummary = summary;
        RebuildTable(summary.Rows, history.ActivePosition);
        SetActivePosition(history.ActivePosition);
    }

    private void SetActivePosition(WatchPosition position)
    {
        _dashboard.ActivePositionText.Text = position.ShortName();
        _dashboard.ActiveOrientationText.Text = position.LongName();
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
}
