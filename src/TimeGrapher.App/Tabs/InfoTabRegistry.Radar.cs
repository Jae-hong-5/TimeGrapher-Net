using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TimeGrapher.App.Rendering;

namespace TimeGrapher.App.Tabs;

internal sealed partial class InfoTabRegistry
{
    // Watch Health radar: a six-position hexagon of the per-position aggregates the
    // frame snapshot already carries, plus a rule-based diagnosis panel. It reuses
    // the same snapshot the Positions tab consumes (Strategy: another consumer over
    // one snapshot), so the analysis engine is untouched — the catalog entry and
    // this factory are the whole addition.
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

        var hint = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        var verdict = new TextBlock { FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        var summary = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var weakest = new TextBlock { FontSize = 13, Margin = new Thickness(0, 6, 0, 0) };

        var renderer = new WatchHealthRadarRenderer(radar, hint, verdict, summary, weakest);

        // Amplitude / Rate / Beat error toggle, styled like the other tab selectors
        // (PositionButton + the shared "active" accent).
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
                FontSize = 12,
                MinHeight = 28,
                Padding = new Thickness(10, 2),
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

        var toggleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggleRow.Children.Add(MetricButton("Amplitude", RadarMetric.Amplitude));
        toggleRow.Children.Add(MetricButton("Rate", RadarMetric.Rate));
        toggleRow.Children.Add(MetricButton("Beat error", RadarMetric.BeatError));
        UpdateMetricButtons();

        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(8, 4, 8, 2),
        };
        Grid.SetColumn(toggleRow, 0);
        Grid.SetColumn(hint, 1);
        headerStrip.Children.Add(toggleRow);
        headerStrip.Children.Add(hint);

        var radarArea = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(headerStrip, 0);
        Grid.SetRow(radar, 1);
        radarArea.Children.Add(headerStrip);
        radarArea.Children.Add(radar);
        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            radarArea.Children.Add(overlay);
        }

        var panelStack = new StackPanel { Margin = new Thickness(12) };
        panelStack.Children.Add(new TextBlock
        {
            Text = "DIAGNOSIS",
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panelStack.Children.Add(verdict);
        panelStack.Children.Add(summary);
        panelStack.Children.Add(weakest);
        panelStack.Children.Add(new TextBlock
        {
            Text = "Rule-based, from the live per-position measurements. Reuses the Positions sequence — no new sensor.",
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var panel = new Border
        {
            Classes = { "PositionPanel" },
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 4, 0),
            Child = panelStack,
        };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300") };
        Grid.SetColumn(radarArea, 0);
        Grid.SetColumn(panel, 1);
        root.Children.Add(radarArea);
        root.Children.Add(panel);

        var consumer = new WatchHealthRadarFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, root), consumer);
    }
}
