using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TimeGrapher.App.Rendering;

namespace TimeGrapher.App.Tabs;

internal sealed partial class InfoTabRegistry
{
    // Watch Health: a six-position hexagon plus a unified Diagnosis rail that judges
    // BOTH axes over the same snapshot the Positions tab consumes (Strategy: another
    // consumer over one snapshot) — band conformance per position (Levels) and
    // cross-position Consistency (the shared ConsistencyDiagnosis). The analysis
    // engine is untouched; the catalog entry, this factory and the rail are the
    // whole addition.
    private static InfoTabRegistration CreateWatchHealthRadarRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var radar = new WatchHealthRadarControl(context.TextFontFamily)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(4),
        };

        // --- Diagnosis rail controls ---
        var overall = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        var overallSub = new TextBlock { FontSize = 14, Opacity = 0.6, Margin = new Thickness(0, 2, 0, 0) };

        var levelsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("64,*,*,*,18"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        levelsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddLevelHeader(levelsGrid, 0, "Position");
        AddLevelHeader(levelsGrid, 1, "Amplitude");
        AddLevelHeader(levelsGrid, 2, "Error Rate");
        AddLevelHeader(levelsGrid, 3, "Beat Error");

        var levelRows = new List<HealthLevelRowControls>(WatchHealthRadarModel.AxisOrder.Count);
        for (int i = 0; i < WatchHealthRadarModel.AxisOrder.Count; i++)
        {
            levelRows.Add(AddLevelRow(levelsGrid, i + 1));
        }

        var weakest = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

        (Border spreadCard, HealthConsistencyRowControls spread) =
            MakeConsistencyCard("D-Spread", "best−worst rate gap · limit 15 s/d");
        (Border balanceCard, HealthConsistencyRowControls balance) =
            MakeConsistencyCard("Balance Wheel", "vertical-position rate spread · limit 15 s/d");
        (Border vhCard, HealthConsistencyRowControls verticalHorizontal) =
            MakeConsistencyCard("V/H Bias", "vertical − horizontal mean rate");

        var rail = new HealthDiagnosisControls(
            overall, overallSub, levelRows, weakest, spread, balance, verticalHorizontal);

        var renderer = new WatchHealthRadarRenderer(radar, rail);

        // --- Amplitude / Rate / Beat metric toggle (in the rail header) ---
        var metricButtons = new List<(Button Button, RadarMetric Metric)>();

        void UpdateMetricButtons()
        {
            foreach ((Button button, RadarMetric metric) in metricButtons)
            {
                bool active = metric == renderer.Metric;
                if (active && !button.Classes.Contains("active"))
                {
                    button.Classes.Add("active");
                }
                else if (!active)
                {
                    button.Classes.Remove("active");
                }
            }
        }

        Button MetricButton(string text, RadarMetric metric)
        {
            var button = new Button
            {
                Content = text,
                FontSize = 14,
                MinHeight = 26,
                Padding = new Thickness(8, 2),
                Margin = new Thickness(0, 0, 4, 0),
            };
            button.Classes.Add("PositionButton");
            ToolTip.SetTip(button, $"Plot {text.ToLowerInvariant()} by position");
            button.Click += (_, _) =>
            {
                renderer.SetMetric(metric);
                UpdateMetricButtons();
            };
            metricButtons.Add((button, metric));
            return button;
        }

        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        toggleRow.Children.Add(MetricButton("Amplitude", RadarMetric.Amplitude));
        toggleRow.Children.Add(MetricButton("Error Rate", RadarMetric.Rate));
        toggleRow.Children.Add(MetricButton("Beat Error", RadarMetric.BeatError));
        UpdateMetricButtons();

        // --- radar area (col 0) ---
        var radarArea = new Grid();
        radarArea.Children.Add(radar);
        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            radarArea.Children.Add(overlay);
        }

        // --- Diagnosis rail (col 1) ---
        var railStack = new StackPanel { Margin = new Thickness(12) };

        var railHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var railTitle = new TextBlock { Text = "Diagnosis", FontSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(railTitle, 0);
        Grid.SetColumn(toggleRow, 1);
        railHeader.Children.Add(railTitle);
        railHeader.Children.Add(toggleRow);
        railStack.Children.Add(railHeader);

        railStack.Children.Add(overall);
        railStack.Children.Add(overallSub);

        railStack.Children.Add(SectionLabel("Levels"));
        railStack.Children.Add(levelsGrid);
        railStack.Children.Add(weakest);

        railStack.Children.Add(SectionLabel("Consistency"));
        railStack.Children.Add(spreadCard);
        railStack.Children.Add(balanceCard);
        railStack.Children.Add(vhCard);

        var panel = new Border
        {
            Classes = { "PositionPanel" },
            Padding = new Thickness(4),
            Margin = new Thickness(2, 0, 4, 0),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = railStack,
            },
        };

        var radarPanel = new Border
        {
            Classes = { "PositionPanel" },
            Padding = new Thickness(4),
            Margin = new Thickness(4, 0, 2, 0),
            Child = radarArea,
        };

        // Rail is wide enough for the 14 px Diagnosis fonts without horizontal
        // clipping (the ScrollViewer disables horizontal scroll, so overflow is
        // cut, not scrolled). The radar keeps the remaining width — it has ample
        // empty margin around the hexagon to spare.
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,540") };
        Grid.SetColumn(radarPanel, 0);
        Grid.SetColumn(panel, 1);
        root.Children.Add(radarPanel);
        root.Children.Add(panel);

        var consumer = new WatchHealthRadarFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, root), consumer);
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Opacity = 0.5,
        Margin = new Thickness(0, 14, 0, 4),
    };

    private static void AddLevelHeader(Grid grid, int column, string text)
    {
        var label = new TextBlock { Text = text, FontSize = 14, Opacity = 0.5, Margin = new Thickness(0, 0, 0, 2) };
        Grid.SetRow(label, 0);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static HealthLevelRowControls AddLevelRow(Grid grid, int rowIndex)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var pos = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 2) };
        var amp = new TextBlock { FontSize = 14, Margin = new Thickness(0, 2, 0, 2) };
        var rate = new TextBlock { FontSize = 14, Margin = new Thickness(0, 2, 0, 2) };
        var beat = new TextBlock { FontSize = 14, Margin = new Thickness(0, 2, 0, 2) };
        var dot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsVisible = false,
        };
        ToolTip.SetTip(dot, "Worst status for this position");

        Place(grid, pos, rowIndex, 0);
        Place(grid, amp, rowIndex, 1);
        Place(grid, rate, rowIndex, 2);
        Place(grid, beat, rowIndex, 3);
        Place(grid, dot, rowIndex, 4);

        return new HealthLevelRowControls(pos, amp, rate, beat, dot);
    }

    private static (Border Card, HealthConsistencyRowControls Controls) MakeConsistencyCard(string name, string sub)
    {
        var nameLabel = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeight.Bold };
        var subLabel = new TextBlock { Text = sub, FontSize = 14, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(nameLabel);
        left.Children.Add(subLabel);

        var reading = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
        var chip = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Place(grid, left, 0, 0);
        Place(grid, reading, 0, 1);
        Place(grid, chip, 0, 2);

        var card = new Border
        {
            Classes = { "PositionPanel" },
            Padding = new Thickness(10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
        };

        return (card, new HealthConsistencyRowControls(reading, chip));
    }

    private static void Place(Grid grid, Control child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
