using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class MultiPositionSeqRenderer
{
    private const string ActiveRowClass = "SeqActiveRow";
    private const string OnAccentClass = "SeqOnAccent";
    private const string ResultOkClass = "ok";
    private const string ResultWarnClass = "warn";
    private const string ResultPendingClass = "pending";
    private const long MinPositionBeatsForVerdict = VarioVerdict.MinSamples;
    private const int MinQualifiedPositionsForVerdict = 3;
    private const int MinVerticalPositionsForBalanceWheelVerdict = 2;

    private static readonly string[] Headers = { "POS", "Error Rate", "Amplitude", "BEAT ERROR", "BEATS" };

    private readonly Grid _tableGrid;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;
    private readonly PositionSequenceDashboardControls _dashboard;
    private readonly WatchPosition _initialPosition;

    private ulong _lastVersion;
    private SequenceSummary _lastSummary = SequenceSummary.Compute(Array.Empty<PositionSummary>());

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
        Reset(_initialPosition);
    }

    public void Reset(WatchPosition activePosition)
    {
        _lastVersion = 0;
        _alertBanner.IsVisible = false;
        _lastSummary = SequenceSummary.Compute(Array.Empty<PositionSummary>());
        RebuildTable(_lastSummary.Rows, activePosition);
        UpdateDashboard(_lastSummary, activePosition);
    }

    public void RequestPosition(WatchPosition position)
    {
        RebuildTable(_lastSummary.Rows, position);
        _dashboard.ActivePositionText.Text = position.ShortName();
        _dashboard.ActiveOrientationText.Text = position.LongName();
        UpdateConsistencyVerdict(_lastSummary, position);
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

        UpdateConsistencyVerdict(summary, activePosition);
        _dashboard.AverageRateText.Text = VarioReadout.Format(summary.RateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.AverageAmplitudeText.Text = VarioReadout.Format(summary.AmplitudeMeanDeg, "0", "°");
        _dashboard.SpreadRateText.Text = VarioReadout.Format(summary.RateSpreadSPerDay, "0.0", " s/d");
        _dashboard.SpreadAmplitudeText.Text = VarioReadout.Format(summary.AmplitudeSpreadDeg, "0", "°");
        _dashboard.BalanceWheelSpreadText.Text = VarioReadout.Format(
            summary.VerticalRateSpreadSPerDay,
            "0.0",
            " s/d");
        _dashboard.VerticalRateText.Text = VarioReadout.Format(summary.VerticalRateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.HorizontalRateText.Text = VarioReadout.Format(summary.HorizontalRateMeanSPerDay, "+0.0;-0.0;0.0", " s/d");
        _dashboard.VerticalHorizontalDeltaText.Text = VarioReadout.Format(
            summary.VerticalHorizontalRateDeltaSPerDay,
            "+0.0;-0.0;0.0",
            " s/d");
    }

    private void UpdateConsistencyVerdict(SequenceSummary summary, WatchPosition activePosition)
    {
        ConsistencyDiagnosis diagnosis = ConsistencyDiagnosis.Compute(summary, activePosition);

        _dashboard.ConsistencyBadge.Classes.Remove(ResultOkClass);
        _dashboard.ConsistencyBadge.Classes.Remove(ResultWarnClass);
        _dashboard.ConsistencyBadge.Classes.Remove(ResultPendingClass);

        _dashboard.ConsistencyVerdictText.Text = diagnosis.VerdictText;
        _dashboard.ConsistencyDetailText.Text = diagnosis.DetailText;
        _dashboard.ConsistencyBadge.Classes.Add(BadgeClass(diagnosis.Level));

        UpdateRequirementGuides(summary.Rows, diagnosis);
    }

    private static string BadgeClass(VarioVerdictLevel level) => level switch
    {
        VarioVerdictLevel.Good => ResultOkClass,
        VarioVerdictLevel.Warn => ResultWarnClass,
        _ => ResultPendingClass,
    };

    private static string StatusText(ConsistencyStatus status) => status switch
    {
        ConsistencyStatus.Ok => "OK",
        ConsistencyStatus.Check => "CHECK",
        ConsistencyStatus.Ready => "READY",
        ConsistencyStatus.Reference => "REFERENCE",
        _ => "COLLECTING",
    };

    private void UpdateRequirementGuides(
        IReadOnlyList<SequencePositionRow> rows,
        ConsistencyDiagnosis diagnosis)
    {
        SequencePositionRow[] qualifiedRows = rows
            .Where(row => row.RateSPerDay != null && row.Beats >= MinPositionBeatsForVerdict)
            .ToArray();
        SequencePositionRow[] fullVerticalRows = qualifiedRows
            .Where(row => !row.Position.IsHorizontal() && !row.Position.IsIntermediate())
            .ToArray();
        SequencePositionRow[] horizontalRows = qualifiedRows
            .Where(row => row.Position.IsHorizontal())
            .ToArray();
        SequencePositionRow[] measuredRows = rows
            .Where(row => row.RateSPerDay != null || row.AmplitudeDeg != null ||
                row.BeatErrorMs != null || row.Beats > 0)
            .ToArray();
        int verticalPositions = fullVerticalRows.Length;

        _dashboard.SpreadStatusText.Text = StatusText(diagnosis.SpreadStatus);
        _dashboard.BalanceStatusText.Text = StatusText(diagnosis.BalanceStatus);
        _dashboard.VerticalHorizontalStatusText.Text = StatusText(diagnosis.VerticalHorizontalStatus);
        _dashboard.AverageStatusText.Text = "REFERENCE";

        _dashboard.SpreadRequirementText.Text =
            $"{MinQualifiedPositionsForVerdict} positions, {MinPositionBeatsForVerdict}+ beats each";
        _dashboard.SpreadReadyText.Text =
            $"{FormatReadyPositions(qualifiedRows)} ({qualifiedRows.Length}/{MinQualifiedPositionsForVerdict})";
        _dashboard.BalanceRequirementText.Text =
            $"{MinVerticalPositionsForBalanceWheelVerdict} full vertical positions, {MinPositionBeatsForVerdict}+ beats each";
        _dashboard.BalanceReadyText.Text =
            $"{FormatReadyPositions(fullVerticalRows)} ({verticalPositions}/{MinVerticalPositionsForBalanceWheelVerdict})";
        _dashboard.VerticalHorizontalRequirementText.Text =
            "1 full vertical + 1 horizontal";
        _dashboard.VerticalHorizontalReadyText.Text =
            FormatVerticalHorizontalReady(fullVerticalRows, horizontalRows);
        _dashboard.AverageRequirementText.Text =
            "any measured position";
        _dashboard.AverageReadyText.Text = FormatReadyPositions(measuredRows);
    }

    private static string FormatReadyPositions(IReadOnlyList<SequencePositionRow> rows) =>
        rows.Count == 0
            ? "None"
            : string.Join(", ", rows.Select(row => row.Position.ShortName()));

    private static string FormatVerticalHorizontalReady(
        IReadOnlyList<SequencePositionRow> fullVerticalRows,
        IReadOnlyList<SequencePositionRow> horizontalRows)
    {
        int verticalPositions = fullVerticalRows.Count;
        int horizontalPositions = horizontalRows.Count;
        return
            $"V {FormatReadyPositions(fullVerticalRows)} / H {FormatReadyPositions(horizontalRows)} " +
            $"({verticalPositions}V + {horizontalPositions}H)";
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
