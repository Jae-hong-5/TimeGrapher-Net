using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class InfoTabRegistryTests
{
    private const double VarioCapturedMinimumFontSize = 14.0;
    private const double PositionHeroDiagramSizeForTest = 160.0;
    private const double PositionHeroMetricLabelFontSizeForTest = 15.0;
    private const double TraceHeaderButtonMinHeightForTest = 30.0;
    private const double TraceHeaderButtonFontSizeForTest = 12.0;
    private const double WaveformDirectionAxisWidthForTest = 58.0;

    public InfoTabRegistryTests() => EnsureAvaloniaPlatform();

    [Fact]
    public void RegistryCreatesCatalogTabsAndConsumers()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();

        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");

        Assert.Equal(InfoTabCatalog.All.Count, registry.Registrations.Count);
        Assert.Equal(InfoTabCatalog.All.Count, tabControl.ItemCount);
        // The registry throws on a kind without a factory; building it from the
        // catalog proves every tab constructs and yields a consumer per id.
        Assert.Equal(InfoTabCatalog.All.Count, registry.Consumers.Count);
        Assert.Equal(
            InfoTabCatalog.All.Select(tab => tab.Id).OrderBy(id => id, StringComparer.Ordinal),
            registry.Consumers.Select(consumer => consumer.TabId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.NotNull(registry.SoundImageControl);
        Assert.All(InfoTabCatalog.All, definition =>
            Assert.Contains(registry.Registrations, registration => registration.Definition.Id == definition.Id));
        Assert.All(InfoTabCatalog.All, definition =>
            Assert.True(registry.CreateRouter().HasConsumer(definition.Id)));
    }

    [Fact]
    public void ResetViewButtonsAdvertiseAllGraphReset()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();

        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        Button[] buttons = ResetViewButtons(registry);

        Assert.Equal(3, buttons.Length);
        Assert.All(buttons, button => Assert.Equal("Reset all graph views", ToolTip.GetTip(button)));
    }

    [Fact]
    public void ResetViewButtonsInvokeAllGraphResetCoordinator()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();

        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        Button[] buttons = ResetViewButtons(registry);
        int sentinelCalls = 0;

        Assert.Equal(7, registry.ResetViews.Count);
        registry.ResetViews.Register(() => sentinelCalls++);

        foreach (Button button in buttons)
        {
            sentinelCalls = 0;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, sentinelCalls);
        }
    }

    [Fact]
    public void WaveformCompareDirectionAxisIsCompactDownArrowUsingPastColor()
    {
        Grid content = CreateWaveformCompareContent();
        var window = new Window { Content = content };
        window.Show();

        try
        {
            Grid axis = Assert.IsType<Grid>(content.Children.Single(child =>
                Grid.GetRow(child) == 1 && Grid.GetColumn(child) == 0));
            TextBlock past = Assert.Single(axis.Children.OfType<TextBlock>(), text => text.Text == "Past");
            Grid arrow = Assert.IsType<Grid>(axis.Children.Single(child => Grid.GetRow(child) == 1));
            var head = Assert.Single(arrow.Children.OfType<Avalonia.Controls.Shapes.Path>());
            var shaft = Assert.Single(arrow.Children.OfType<Avalonia.Controls.Shapes.Rectangle>());
            ISolidColorBrush pastBrush = Assert.IsAssignableFrom<ISolidColorBrush>(past.Foreground);
            ISolidColorBrush headBrush = Assert.IsAssignableFrom<ISolidColorBrush>(head.Fill);
            ISolidColorBrush shaftBrush = Assert.IsAssignableFrom<ISolidColorBrush>(shaft.Fill);

            Assert.Equal(WaveformDirectionAxisWidthForTest, axis.Width);
            Assert.Equal(new Thickness(2, 12, 2, 28), axis.Margin);
            Assert.Equal(0, Grid.GetRow(shaft));
            Assert.Equal(1, Grid.GetRow(head));
            Geometry headGeometry = Assert.IsAssignableFrom<Geometry>(head.Data);
            Assert.True(headGeometry.FillContains(new Point(2, 2)));
            Assert.False(headGeometry.FillContains(new Point(2, 11)));
            Assert.Equal(past.Opacity, arrow.Opacity);
            Assert.Equal(pastBrush.Color, headBrush.Color);
            Assert.Equal(pastBrush.Color, shaftBrush.Color);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void RegistryCreatesAlwaysVisiblePositionStrip()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();

        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");

        InfoTabRegistration registration = Assert.Single(
            registry.Registrations,
            registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId);
        var content = Assert.IsType<Grid>(registration.TabItem.Content);
        Button[] buttons = positionStrip.Children.OfType<Button>().ToArray();

        Assert.Empty(content.ColumnDefinitions);
        Assert.Single(positionStrip.ColumnDefinitions);
        Assert.Equal(WatchPositions.Count, positionStrip.RowDefinitions.Count);
        Assert.Equal(WatchPositions.Count, buttons.Length);
        WatchModelView[] diagrams = Descendants(content).OfType<WatchModelView>().ToArray();
        WatchModelView activeDiagram = Assert.Single(diagrams);

        Assert.Equal(WatchPosition.CH, activeDiagram.Position);
        Assert.Equal(PositionHeroDiagramSizeForTest, activeDiagram.Width);
        Assert.Equal(PositionHeroDiagramSizeForTest, activeDiagram.Height);
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text => text.Text == "POSITION MAP");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "Amplitude");
        Grid tableGrid = Assert.Single(Descendants(content).OfType<Grid>(), grid =>
            grid.ColumnDefinitions.Count == 7 &&
            Descendants(grid).OfType<TextBlock>().Any(text => text.Text == "Pos."));
        Assert.Equal(WatchPositions.Count + 1, tableGrid.RowDefinitions.Count);
        Assert.DoesNotContain(Descendants(content).OfType<Border>(), border =>
            border.Classes.Contains("PositionMapTile"));
        Grid heroReadouts = Assert.Single(Descendants(content).OfType<Grid>(), grid =>
            grid.ColumnDefinitions.Count == 4 &&
            grid.Children.OfType<StackPanel>().Count() == 4 &&
            Descendants(grid).OfType<TextBlock>().Any(text => text.Text == "Beat Error"));
        var heroReadoutHost = Assert.IsType<Grid>(heroReadouts.Parent);
        Assert.Equal(new Thickness(24, 0, 18, 0), heroReadoutHost.Margin);
        Border heroCard = Assert.Single(Descendants(content).OfType<Border>(), border =>
            border.Classes.Contains("PositionPanel") &&
            border.Child is Grid child &&
            Descendants(child).OfType<WatchModelView>().Any());
        Assert.Equal(new Thickness(4, 0, 8, 12), heroCard.Margin);
        Assert.All(heroReadouts.ColumnDefinitions, column =>
            Assert.True(column.Width.IsStar && column.Width.Value == 1.0));
        Assert.All(heroReadouts.Children.OfType<StackPanel>(), readout =>
        {
            Assert.Equal(HorizontalAlignment.Stretch, readout.HorizontalAlignment);
            TextBlock[] readoutTexts = readout.Children.OfType<TextBlock>().ToArray();
            Assert.Equal(2, readoutTexts.Length);
            Assert.All(readoutTexts, text =>
            {
                Assert.Equal(HorizontalAlignment.Stretch, text.HorizontalAlignment);
                Assert.Equal(TextAlignment.Center, text.TextAlignment);
            });
            TextBlock label = readoutTexts[0];
            Assert.Equal(PositionHeroMetricLabelFontSizeForTest, label.FontSize);
            Assert.True(label.IsSet(TextBlock.ForegroundProperty));
        });
        for (int i = 0; i < buttons.Length; i++)
        {
            Assert.Equal(i, Grid.GetRow(buttons[i]));
            Assert.Equal(VerticalAlignment.Stretch, buttons[i].VerticalAlignment);
        }
        Assert.Single(registry.Consumers, consumer => consumer.TabId == InfoTabCatalog.WatchPositionsTabId);
    }

    private static void EnsureAvaloniaPlatform() => HeadlessPlatform.EnsureStarted();


    [Fact]
    public void PositionStripObservesFramesEvenWhenPositionsTabIsInactive()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        Button[] buttons = positionStrip.Children.OfType<Button>().ToArray();
        AnalysisFrameRouter router = registry.CreateRouter();

        Assert.Equal(WatchPositions.Count, buttons.Length);
        router.Route(
            new AnalysisFrame
            {
                MetricsHistory = new BeatMetricsHistorySnapshot
                {
                    Version = 1,
                    ActivePosition = WatchPosition.P6H,
                },
            },
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Button activeButton = Assert.Single(buttons, button => button.Classes.Contains("active"));
        TextBlock activeButtonText = Assert.IsType<TextBlock>(activeButton.Content);
        Assert.Equal("6H", activeButtonText.Text);
        Assert.Equal(6, Grid.GetRow(activeButton));
        WatchModelView diagram = Assert.Single(Descendants(
            Assert.IsType<Grid>(registry.Registrations.Single(
                registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content))
            .OfType<WatchModelView>());
        Assert.Equal(WatchPosition.P6H, diagram.Position);
    }

    [Fact]
    public void ActivePositionButtonStillColorsOnlyItsOwnLabelWhite()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        var window = new Window { Content = positionStrip };
        window.Show();

        try
        {
            registry.CreateRouter().Route(
                new AnalysisFrame
                {
                    MetricsHistory = new BeatMetricsHistorySnapshot
                    {
                        Version = 1,
                        ActivePosition = WatchPosition.P6H,
                    },
                },
                InfoTabCatalog.WatchPositionsTabId,
                new AnalysisTabRenderContext(48000));

            Button activeButton = Assert.Single(positionStrip.Children.OfType<Button>(),
                button => button.Classes.Contains("active"));
            TextBlock activeButtonText = Assert.IsType<TextBlock>(activeButton.Content);

            Assert.Equal(Brushes.White, activeButtonText.Foreground);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void PositionButtonClickBeforePlayUpdatesPositionTabText()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        var content = Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content);
        Button target = positionStrip.Children
            .OfType<Button>()
            .Single(button => button.Content is TextBlock { Text: "7:30H" });

        target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        WatchModelView diagram = Assert.Single(Descendants(content).OfType<WatchModelView>());
        Assert.Equal(WatchPosition.P9H45, diagram.Position);
        Border activeRow = Assert.Single(Descendants(content).OfType<Border>(),
            border => border.Classes.Contains("SeqActiveRow"));
        Assert.Equal(8, Grid.GetRow(activeRow));
    }

    [Fact]
    public void PositionTabResetKeepsSelectedPositionTextAndDiagram()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        var content = Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content);
        Button target = positionStrip.Children
            .OfType<Button>()
            .Single(button => button.Content is TextBlock { Text: "1:30H" });

        target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        IAnalysisFrameConsumer consumer = registry.Consumers.Single(
            consumer => consumer.TabId == InfoTabCatalog.WatchPositionsTabId);
        consumer.Reset(new AnalysisTabResetContext(
            SampleRate: 48000,
            RateErrorYScale: 250.0,
            RateDataPoints: 500,
            ActivePosition: WatchPosition.P3H45));

        WatchModelView diagram = Assert.Single(Descendants(content).OfType<WatchModelView>());
        Assert.Equal(WatchPosition.P3H45, diagram.Position);
        Button activeButton = Assert.Single(positionStrip.Children.OfType<Button>(),
            button => button.Classes.Contains("active"));
        Assert.IsType<TextBlock>(activeButton.Content);
        Assert.Equal("1:30H", ((TextBlock)activeButton.Content).Text);
    }

    [Fact]
    public void PositionsTabHeroAndTableReflectMeasuredPositions()
    {
        EnsureAvaloniaPlatform();
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        var content = Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content);
        AnalysisFrameRouter router = registry.CreateRouter();

        router.Route(
            Frame(
                version: 1,
                activePosition: WatchPosition.CH,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 45),
                Position(WatchPosition.P3H, rate: 0.0, amplitude: 260.0, count: 30)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        List<TextBlock> texts = Descendants(content).OfType<TextBlock>().ToList();
        // ACTIVE hero live readout (CH) — also appears in the CH table row.
        Assert.Contains(texts, text => text.Text == "300°");
        Assert.Contains(texts, text => text.Text == "45");
        Assert.Contains(texts, text => text.Text == "Rate Range");
        Assert.DoesNotContain(texts, text => text.Text == "Cur. Position");
        Assert.DoesNotContain(texts, text => text.Text == "45 / 30 beats");
        Assert.DoesNotContain(texts, text => text.Text == "280°");
        Assert.DoesNotContain(texts, text => text.Text == "2 / 10");
        Assert.DoesNotContain(texts, text => text.Text == "75");
        Assert.DoesNotContain(texts, text => text.Text == "Avg. Error Rate");
        Assert.DoesNotContain(texts, text => text.Text == "Avg. Amplitude");
        Assert.DoesNotContain(texts, text => text.Text == "Positions");
        Assert.DoesNotContain(texts, text => text.Text == "Total Beats");
        Assert.DoesNotContain(texts, text => text.Text == "Error Rate vs Band");
        Assert.DoesNotContain(texts, text => text.Text == "Band");
        Assert.DoesNotContain(texts, text => text.Text == "Mean Out");
        Assert.DoesNotContain(texts, text => text.Text == "30+ Beats");
        Assert.DoesNotContain(texts, text => text.Text == "Collecting");
        Assert.DoesNotContain(texts, text => text.Text is "not measured" or "30+ beats");
        // One acquisition (rate-range) lane per position row.
        Assert.Equal(WatchPositions.Count, Descendants(content).OfType<RateRangeLaneControl>().Count());
    }

    private static StatsSummary Stats(double mean, long count) =>
        new(Valid: true, Min: mean, Max: mean, Mean: mean, Sigma: 0.0, Count: count);

    private static PositionSummary Position(
        WatchPosition position,
        double? rate = null,
        double? amplitude = null,
        double? beatError = null,
        long count = 30) => new(
        position,
        rate is double r ? Stats(r, count) : default,
        amplitude is double a ? Stats(a, count / 2) : default,
        beatError is double b ? Stats(b, count) : default);

    private static AnalysisFrame Frame(
        ulong version,
        WatchPosition activePosition,
        params PositionSummary[] positions) => new()
    {
        MetricsHistory = new BeatMetricsHistorySnapshot
        {
            Version = version,
            ActivePosition = activePosition,
            Positions = positions,
        },
    };

    [Fact]
    public void VarioSummaryShowsVerdictsWithoutNumericSublines()
    {
        Grid content = CreateVarioContent();
        var summaryCard = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        var summaryStack = Assert.IsType<StackPanel>(summaryCard.Child);
        var overallText = Assert.IsType<TextBlock>(summaryStack.Children[0]);
        var summaryColumns = Assert.IsType<Grid>(summaryStack.Children[1]);
        StackPanel[] measureColumns = summaryColumns.Children
            .OfType<StackPanel>()
            .Where(column => Grid.GetColumn(column) is 0 or 1)
            .ToArray();

        Assert.Equal(2, measureColumns.Length);
        Assert.All(measureColumns, column => Assert.Equal(2, column.Children.Count));
        Assert.True(overallText.MinHeight >= 22);
        Assert.True(overallText.IsVisible);
        Assert.All(
            measureColumns.Select(column => Assert.IsType<TextBlock>(column.Children[1])),
            status => Assert.Equal(VarioCapturedMinimumFontSize, status.FontSize));
        Assert.Equal(" ", overallText.Text);
        Assert.True(summaryCard.Padding.Bottom <= 4);
        Assert.DoesNotContain(
            Descendants(summaryCard).OfType<TextBlock>(),
            text => text.Text == "VARIO SUMMARY");
    }

    [Fact]
    public void VarioTabBordersUseSquareCorners()
    {
        // Guards against re-introducing a local CornerRadius on a Vario border,
        // which would override the global App.axaml Border style and render
        // rounded. This only proves no local radius is set (App.axaml styles are
        // not applied in this unit context); the Border { CornerRadius=0 } rule
        // itself is asserted in AppXamlLoadTests.AppAxamlEnforcesSquareCorners.
        Grid content = CreateVarioContent();
        Border[] borders = Descendants(content).OfType<Border>().ToArray();

        Assert.NotEmpty(borders);
        Assert.All(borders, border => Assert.Equal(new CornerRadius(0), border.CornerRadius));
    }

    [Fact]
    public void VarioCriteriaFlyoutWrapsRuleText()
    {
        Grid content = CreateVarioContent();
        Button criteriaButton = Descendants(content)
            .OfType<Button>()
            .Single(button => Equals(button.Content, "Criteria ▾"));
        var flyout = Assert.IsType<Flyout>(criteriaButton.Flyout);
        Assert.Equal(PlacementMode.BottomEdgeAlignedRight, flyout.Placement);
        var panel = Assert.IsType<StackPanel>(flyout.Content);
        Assert.True(panel.Width <= 360);
        TextBlock[] rules = panel.Children
            .OfType<TextBlock>()
            .Where(text => text.Text is { } value &&
                (value.StartsWith("Within Band:", StringComparison.Ordinal) ||
                 value.StartsWith("Fast / Slow · out of range:", StringComparison.Ordinal) ||
                 value.StartsWith("Healthy:", StringComparison.Ordinal) ||
                 value.StartsWith("Slightly low / High:", StringComparison.Ordinal) ||
                 value.StartsWith("Low · service:", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(5, rules.Length);
        Assert.All(rules, rule =>
        {
            Assert.Equal(TextWrapping.Wrap, rule.TextWrapping);
            Assert.True(rule.MaxWidth <= 320);
        });
    }

    [Fact]
    public void VarioCriteriaGuideSitsInSummaryWithoutElapsedReadout()
    {
        Grid content = CreateVarioContent();
        var summaryCard = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        var summaryStack = Assert.IsType<StackPanel>(summaryCard.Child);
        var summaryColumns = Assert.IsType<Grid>(summaryStack.Children[1]);
        Button criteriaButton = Assert.IsType<Button>(
            summaryColumns.Children.Single(child => Grid.GetColumn(child) == 2));

        Assert.Equal("Criteria ▾", criteriaButton.Content);
        Assert.True(criteriaButton.FontSize >= VarioCapturedMinimumFontSize);
        Assert.True(criteriaButton.MinWidth >= 128);
        Assert.True(criteriaButton.MinHeight >= 30);
        Assert.Equal(HorizontalAlignment.Left, criteriaButton.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, criteriaButton.VerticalAlignment);
        Assert.DoesNotContain(
            Descendants(content).OfType<TextBlock>(),
            text => text.Text == "Elapsed");
    }

    [Fact]
    public void VarioStatsTableHeadersAreHighContrast()
    {
        Grid content = CreateVarioContent();
        Border statsBorder = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 1));
        var statsTable = Assert.IsType<Grid>(statsBorder.Child);
        TextBlock[] headers = statsTable.Children
            .OfType<TextBlock>()
            .Where(text => Grid.GetRow(text) == 0)
            .ToArray();
        TextBlock[] rowLabels = statsTable.Children
            .OfType<TextBlock>()
            .Where(text => Grid.GetColumn(text) == 0 && Grid.GetRow(text) is 1 or 2)
            .ToArray();

        Assert.Equal(6, headers.Length);
        Assert.Equal(new[] { "Error Rate", "Amplitude" }, rowLabels.Select(label => label.Text).ToArray());
        Assert.True(statsTable.ColumnDefinitions[0].Width.Value >= 104);
        Assert.All(headers, header =>
        {
            Assert.True(header.Opacity >= 0.8);
            Assert.Equal(FontWeight.SemiBold, header.FontWeight);
            Assert.True(header.FontSize >= VarioCapturedMinimumFontSize);
        });
    }

    [Fact]
    public void VarioTextUsesCapturedMinimumFontSize()
    {
        Grid content = CreateVarioContent();
        Button criteriaButton = Descendants(content)
            .OfType<Button>()
            .Single(button => Equals(button.Content, "Criteria ▾"));
        var flyout = Assert.IsType<Flyout>(criteriaButton.Flyout);
        var criteriaPanel = Assert.IsType<StackPanel>(flyout.Content);

        TextBlock[] textBlocks = Descendants(content)
            .Concat(Descendants(criteriaPanel))
            .OfType<TextBlock>()
            .ToArray();

        Assert.NotEmpty(textBlocks);
        Assert.All(textBlocks, text => Assert.True(text.FontSize >= VarioCapturedMinimumFontSize));
    }

    [Fact]
    public void VarioUsesAxisLabelsInsteadOfGaugeHeaders()
    {
        InfoTabRegistration registration = CreateVarioRegistration();
        Grid content = Assert.IsType<Grid>(registration.TabItem.Content);

        registration.Consumer.Initialize(new AnalysisTabResetContext(48000, 10, 250));

        var ratePlot = Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 2));
        var amplitudePlot = Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 3));

        Assert.Equal(4, content.RowDefinitions.Count);
        Assert.Equal("Error Rate (s/d)", ratePlot.Plot.Axes.Bottom.Label.Text);
        Assert.Equal("Amplitude (°)", amplitudePlot.Plot.Axes.Bottom.Label.Text);
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text => text.Text == "Error Rate (s/d)");
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text => text.Text == "Amplitude (°)");
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text =>
            text.Text?.StartsWith("Acceptable band", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void LongTermTabUsesCompactShortWindowNavigation()
    {
        Grid content = CreateLongTermContent();
        var header = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        string[] buttons = Descendants(header)
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();

        Assert.Equal(new[] { "1h", "3h", "6h", "‹", "›", "Reset View" }, buttons);
        Assert.DoesNotContain(Descendants(header).OfType<TextBlock>(), text => text.Text == "24H LONG-TERM");
        Assert.Contains(Descendants(header).OfType<TextBlock>(), text => text.Text == "Collecting");
        Assert.Contains(Descendants(header).OfType<TextBlock>(), text => text.Text == "Error Rate —");
        Button resetView = Descendants(header).OfType<Button>().Single(button => Equals(button.Content, "Reset View"));
        Assert.Contains("PositionButton", resetView.Classes);
        Assert.Equal(TraceHeaderButtonFontSizeForTest, resetView.FontSize);
        Assert.Equal(TraceHeaderButtonMinHeightForTest, resetView.MinHeight);
        Assert.DoesNotContain(Descendants(header).OfType<Button>(), button =>
            !Equals(button.Content, "Reset View") && button.Classes.Contains("active"));
        Assert.DoesNotContain(Descendants(header).OfType<TextBlock>(), text => text.Text?.Contains("Elapsed", StringComparison.Ordinal) == true);
        Assert.Empty(header.RowDefinitions);
        Assert.Equal(5, content.RowDefinitions.Count);
        // Beat-error row (3) is enlarged to offset its visible time-axis so all three
        // data areas match; a revert to '*' would silently break equal heights.
        Assert.True(content.RowDefinitions[3].Height.IsStar);
        Assert.Equal(1.22, content.RowDefinitions[3].Height.Value, 3);
        Assert.Equal(GridUnitType.Auto, content.RowDefinitions[4].Height.GridUnitType);
        Assert.Equal(1.0, content.RowDefinitions[1].Height.Value, 3);
        Assert.Equal(new Thickness(0, 0, 0, -8), Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 1)).Margin);
        Assert.Equal(new Thickness(0, -4, 0, -4), Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 2)).Margin);
        Assert.Equal(new Thickness(0, -8, 0, 0), Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 3)).Margin);
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text =>
            text.Text?.StartsWith("Shaded band =", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void TraceTabSharesTimeAxisAndEnlargesAmplitudeRow()
    {
        Grid content = CreateTraceContent();

        // Amplitude (row 2) is enlarged 1.11x so both data areas stay equal height
        // once the rate pane's X axis is hidden (shared bottom time axis).
        Assert.Equal(1.0, content.RowDefinitions[1].Height.Value, 3);
        Assert.True(content.RowDefinitions[2].Height.IsStar);
        Assert.Equal(1.11, content.RowDefinitions[2].Height.Value, 3);
    }

    [Fact]
    public void TraceTabReservesAlertBannerSpaceBesideHeaderButtons()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateTraceContent();
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        // The strip holds the banner in a star (reserved) column and the always-on
        // button strip in an auto column, so it keeps its height and the plots
        // below never shift when the banner toggles.
        Assert.Equal(2, headerStrip.ColumnDefinitions.Count);
        Assert.True(headerStrip.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[1].Width.GridUnitType);

        var buttonStrip = headerStrip.Children.OfType<StackPanel>().Single();
        Assert.Equal(1, Grid.GetColumn(buttonStrip));
        string[] buttons = buttonStrip.Children
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "Smoothing" }, buttons);
        Button smoothing = buttonStrip.Children.OfType<Button>().Single();
        Assert.Contains("PositionButton", smoothing.Classes);
        Assert.Equal(TraceHeaderButtonFontSizeForTest, smoothing.FontSize);
        Assert.Equal(TraceHeaderButtonMinHeightForTest, smoothing.MinHeight);
        Assert.True(smoothing.IsVisible);

        Border banner = headerStrip.Children.OfType<Border>().Single();
        Assert.Equal(0, Grid.GetColumn(banner));
        Assert.Equal(TraceHeaderButtonMinHeightForTest, banner.MinHeight);
        Assert.Equal(smoothing.MinHeight, banner.MinHeight);
        // Hidden until the renderer sets a message; it occupies the reserved left
        // column rather than collapsing the whole strip.
        Assert.False(banner.IsVisible);
        // A gap is kept between the banner and the buttons so it never butts up
        // against Smoothing when shown.
        Assert.True(banner.Margin.Right > 0);
        AssertVisibleAlertBannerHeightMatchesButton(content, "Smoothing");
    }

    [Fact]
    public void RateScopeTabUsesSingleReservedHeaderResetButton()
    {
        Grid content = CreateRateScopeContent();
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        Assert.Equal(3, content.RowDefinitions.Count);
        Assert.Equal(GridUnitType.Auto, content.RowDefinitions[0].Height.GridUnitType);
        Assert.True(content.RowDefinitions[1].Height.IsStar);
        Assert.True(content.RowDefinitions[2].Height.IsStar);
        Assert.Equal(2, headerStrip.ColumnDefinitions.Count);
        Assert.True(headerStrip.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[1].Width.GridUnitType);

        StackPanel controls = headerStrip.Children.OfType<StackPanel>().Single();
        Assert.Equal(1, Grid.GetColumn(controls));
        Button resetView = controls.Children.OfType<Button>().Single(button => Equals(button.Content, "Reset View"));
        Assert.Equal("Reset View", resetView.Content);
        Assert.Contains("PositionButton", resetView.Classes);
        Assert.Equal(TraceHeaderButtonFontSizeForTest, resetView.FontSize);
        Assert.Equal(TraceHeaderButtonMinHeightForTest, resetView.MinHeight);

        Assert.Equal(2, content.Children.OfType<AvaPlot>().Count());
        Assert.DoesNotContain(content.Children.OfType<Button>(), button =>
            Grid.GetRow(button) == 1 || Grid.GetRow(button) == 2);
        Assert.Single(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Reset View"));
    }

    [Fact]
    public void ScopeSweepTabUsesReservedHeaderButtonGroup()
    {
        Grid content = CreateScopeSweepContent(new MainWindowViewModel());
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        Assert.Equal(3, content.RowDefinitions.Count);
        Assert.Equal(GridUnitType.Auto, content.RowDefinitions[0].Height.GridUnitType);
        Assert.True(content.RowDefinitions[1].Height.IsStar);
        Assert.Equal(GridUnitType.Auto, content.RowDefinitions[2].Height.GridUnitType);
        Assert.Equal(2, headerStrip.ColumnDefinitions.Count);
        Assert.True(headerStrip.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[1].Width.GridUnitType);

        var buttonStrip = headerStrip.Children.OfType<StackPanel>().Single();
        Assert.Equal(1, Grid.GetColumn(buttonStrip));
        string[] buttons = buttonStrip.Children
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "1-cycle", "2-cycle", "3-cycle", "Reset View" }, buttons);

        Button resetView = buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "Reset View"));
        Assert.All(buttonStrip.Children.OfType<Button>(), button =>
        {
            Assert.Contains("PositionButton", button.Classes);
            Assert.Equal(resetView.FontSize, button.FontSize);
            Assert.Equal(resetView.MinHeight, button.MinHeight);
            Assert.Equal(resetView.Padding, button.Padding);
        });
        Assert.Contains("active", buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "1-cycle")).Classes);
        Assert.DoesNotContain("active", buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "2-cycle")).Classes);
        Assert.DoesNotContain("active", buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "3-cycle")).Classes);

        Assert.DoesNotContain(content.Children.OfType<Button>(), button => Grid.GetRow(button) == 1);
        var readoutGrid = Assert.IsType<Grid>(
            content.Children.Single(child => child is Grid && Grid.GetRow(child) == 2));
        Assert.Equal(ScopeSweepReadout.Labels.Length, readoutGrid.Children.OfType<StackPanel>().Count());
    }

    [Fact]
    public void SpectrogramTabUsesScopeSweepHeaderButtonGroup()
    {
        Grid content = CreateSpectrogramContent();
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        Assert.Equal(6, content.RowDefinitions.Count); // 28677d6 added the beat-direction strip row
        Assert.Equal(GridUnitType.Auto, content.RowDefinitions[0].Height.GridUnitType);
        Assert.True(content.RowDefinitions[1].Height.IsStar);
        Assert.Equal(2, headerStrip.ColumnDefinitions.Count);
        Assert.True(headerStrip.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[1].Width.GridUnitType);
        Assert.Equal(new Thickness(8, 1, 8, 2), headerStrip.Margin);
        Assert.Equal(4, Grid.GetColumnSpan(headerStrip));

        var buttonStrip = headerStrip.Children.OfType<StackPanel>().Single();
        Assert.Equal(1, Grid.GetColumn(buttonStrip));
        Assert.Equal(Orientation.Horizontal, buttonStrip.Orientation);
        Assert.Equal(6, buttonStrip.Spacing);
        Assert.Equal(HorizontalAlignment.Right, buttonStrip.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, buttonStrip.VerticalAlignment);

        string[] buttons = buttonStrip.Children
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "Last Beat", "Compare Beats", "Seconds", "−", "+" }, buttons);

        Assert.All(buttonStrip.Children.OfType<Button>(), button =>
        {
            Assert.Contains("PositionButton", button.Classes);
            Assert.Equal(TraceHeaderButtonFontSizeForTest, button.FontSize);
            Assert.Equal(TraceHeaderButtonMinHeightForTest, button.MinHeight);
            Assert.Equal(TraceHeaderButtonMinHeightForTest, button.Height);
            Assert.Equal(36, button.MinWidth);
            Assert.Equal(new Thickness(10, 2, 10, 2), button.Padding);
            Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalAlignment);
        });

        Border secondsReadout = buttonStrip.Children.OfType<Border>().Single();
        Assert.Equal(TraceHeaderButtonMinHeightForTest, secondsReadout.Height);
        Assert.Equal(TraceHeaderButtonMinHeightForTest, secondsReadout.MinHeight);
        Assert.Equal(44, secondsReadout.MinWidth);
        Assert.Equal(new Thickness(2, 0, 2, 0), secondsReadout.Margin);

        TextBlock secondsText = Assert.IsType<TextBlock>(secondsReadout.Child);
        Assert.Equal("1 s", secondsText.Text);
        Assert.Equal(TraceHeaderButtonFontSizeForTest, secondsText.FontSize);
        Assert.Equal(HorizontalAlignment.Center, secondsText.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, secondsText.VerticalAlignment);
        Assert.Equal(TextAlignment.Center, secondsText.TextAlignment);
        Assert.Contains("active", buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "Seconds")).Classes);
        Assert.DoesNotContain("active", buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "Last Beat")).Classes);
        Assert.DoesNotContain("active", buttonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "Compare Beats")).Classes);
    }

    [Fact]
    public void BeatNoiseTabUsesScopeSweepHeaderButtonGroup()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateBeatNoiseContent(new MainWindowViewModel());
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        Assert.Equal(4, content.RowDefinitions.Count);
        Assert.Equal(GridUnitType.Auto, content.RowDefinitions[0].Height.GridUnitType);
        Assert.True(content.RowDefinitions[1].Height.IsStar);
        Assert.Equal(3, headerStrip.ColumnDefinitions.Count);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[0].Width.GridUnitType);
        Assert.True(headerStrip.ColumnDefinitions[1].Width.IsStar);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[2].Width.GridUnitType);
        Assert.Equal(new Thickness(8, 1, 8, 2), headerStrip.Margin);

        var buttonStrips = headerStrip.Children.OfType<StackPanel>().ToArray();
        Assert.Equal(2, buttonStrips.Length);
        StackPanel modeButtonStrip = buttonStrips.Single(strip => Grid.GetColumn(strip) == 0);
        StackPanel controlButtonStrip = buttonStrips.Single(strip => Grid.GetColumn(strip) == 2);
        Assert.Equal(Orientation.Horizontal, modeButtonStrip.Orientation);
        Assert.Equal(Orientation.Horizontal, controlButtonStrip.Orientation);
        Assert.Equal(6, modeButtonStrip.Spacing);
        Assert.Equal(6, controlButtonStrip.Spacing);
        Assert.Equal(new Thickness(BeatNoiseScopeRenderer.StripLeftAxisSizePx, 0, 0, 0), modeButtonStrip.Margin);
        Assert.Equal(new Thickness(), controlButtonStrip.Margin);
        Assert.Equal(HorizontalAlignment.Left, modeButtonStrip.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Right, controlButtonStrip.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, modeButtonStrip.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, controlButtonStrip.VerticalAlignment);
        string[] modeButtons = modeButtonStrip.Children
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        string[] controlButtons = controlButtonStrip.Children
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "Scope", "Avg Envelope" }, modeButtons);
        Assert.Equal(new[] { "20 ms", "200 ms", "400 ms", "ABS", "Σ" }, controlButtons);

        Assert.All(buttonStrips.SelectMany(strip => strip.Children.OfType<Button>()), button =>
        {
            Assert.Contains("PositionButton", button.Classes);
            Assert.Equal(TraceHeaderButtonFontSizeForTest, button.FontSize);
            Assert.Equal(TraceHeaderButtonMinHeightForTest, button.MinHeight);
            Assert.Equal(TraceHeaderButtonMinHeightForTest, button.Height);
            Assert.Equal(new Thickness(10, 2, 10, 2), button.Padding);
            Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalAlignment);
        });
        Assert.Contains("active", modeButtonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "Scope")).Classes);
        Assert.Contains("active", controlButtonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "20 ms")).Classes);
        Assert.DoesNotContain("active", controlButtonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "400 ms")).Classes);
        Assert.DoesNotContain("active", modeButtonStrip.Children.OfType<Button>().Single(button => Equals(button.Content, "Avg Envelope")).Classes);
        Assert.DoesNotContain(buttonStrips.SelectMany(strip => strip.Children.OfType<TextBlock>()), text => text.Text?.Contains("LIFT") == true);
    }

    [Fact]
    public void FilterScopeTabUsesCompactLaneGridWithoutResetButton()
    {
        Grid content = CreateMultiFilterScopeContent();
        Grid lanesGrid = Assert.IsType<Grid>(content.Children.OfType<Grid>().Single());

        Assert.Empty(content.RowDefinitions);
        Assert.Equal(2, lanesGrid.ColumnDefinitions.Count);
        Assert.Equal(4, lanesGrid.RowDefinitions.Count);
        Assert.DoesNotContain(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Reset View"));
    }

    [Fact]
    public void FilterScopeTabShowsWaitingOverlayBeforeBeatSync()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateMultiFilterScopeContent(new MainWindowViewModel());

        TextBlock overlay = Descendants(content)
            .OfType<TextBlock>()
            .Single(text => text.Text == "Waiting for Tic/Toc sync…");

        Assert.Equal(0, Grid.GetRow(overlay));
    }

    [Fact]
    public void ScopeSweepReferenceValuesUseEscapementReadoutLayout()
    {
        Grid content = CreateScopeSweepContent(new MainWindowViewModel());
        var readoutGrid = Assert.IsType<Grid>(
            content.Children.Single(child => child is Grid && Grid.GetRow(child) == 2));
        StackPanel[] cells = readoutGrid.Children.OfType<StackPanel>().ToArray();
        TextBlock[] titles = cells
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[0]))
            .ToArray();
        TextBlock[] values = cells
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[1]))
            .ToArray();

        Assert.Equal(4, readoutGrid.ColumnDefinitions.Count);
        Assert.All(readoutGrid.ColumnDefinitions, column =>
            Assert.True(column.Width.IsStar && column.Width.Value == 1.0));
        Assert.Equal(ScopeSweepReadout.Labels, titles.Select(title => title.Text).ToArray());
        Assert.All(cells, cell =>
        {
            Assert.Equal(Orientation.Vertical, cell.Orientation);
            Assert.Equal(HorizontalAlignment.Center, cell.HorizontalAlignment);
            Assert.Equal(new Thickness(8, 2, 8, 2), cell.Margin);
        });
        Assert.All(titles, title =>
        {
            Assert.Equal(14, title.FontSize);
            Assert.Equal(FontWeight.Bold, title.FontWeight);
            Assert.Equal(HorizontalAlignment.Center, title.HorizontalAlignment);
            Assert.Equal(TextAlignment.Center, title.TextAlignment);
            Assert.True(title.IsSet(TextBlock.ForegroundProperty));
        });
        Assert.All(values, value =>
        {
            Assert.Equal(VarioReadout.Missing, value.Text);
            Assert.Equal(16, value.FontSize);
            Assert.Equal(FontWeight.Bold, value.FontWeight);
            Assert.Equal(HorizontalAlignment.Center, value.HorizontalAlignment);
            Assert.Equal(TextAlignment.Center, value.TextAlignment);
        });
    }

    [Fact]
    public void ScopeSweepReferenceValuesDoNotResizePlotRow()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateScopeSweepContent(new MainWindowViewModel());
        var contentSize = new Size(1280, 680);
        var sweepPlot = Assert.IsType<AvaPlot>(content.Children.Single(child =>
            child is AvaPlot && Grid.GetRow(child) == 1));
        var readoutGrid = Assert.IsType<Grid>(
            content.Children.Single(child => child is Grid && Grid.GetRow(child) == 2));
        TextBlock[] values = readoutGrid.Children
            .OfType<StackPanel>()
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[1]))
            .ToArray();

        foreach (TextBlock value in values)
        {
            value.Text = "short";
        }
        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));
        Rect shortBounds = sweepPlot.Bounds;

        foreach (TextBlock value in values)
        {
            value.Text = "+1234.5 s/d";
        }
        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));

        Assert.Equal(shortBounds, sweepPlot.Bounds);
    }

    [Fact]
    public void BeatErrorDiagTabReservesAlertBannerSpaceWithoutZoomButtons()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateBeatErrorDiagContent();
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        Assert.Equal(3, content.RowDefinitions.Count);
        ColumnDefinition column = Assert.Single(headerStrip.ColumnDefinitions);
        Assert.True(column.Width.IsStar);
        Assert.Equal(TraceHeaderButtonMinHeightForTest, headerStrip.MinHeight);

        Border banner = headerStrip.Children.OfType<Border>().Single();
        Assert.Equal(0, Grid.GetColumn(banner));
        Assert.Equal(TraceHeaderButtonMinHeightForTest, banner.MinHeight);
        Assert.False(banner.IsVisible);
        Assert.Empty(headerStrip.Children.OfType<StackPanel>());
        Assert.DoesNotContain(Descendants(headerStrip).OfType<Button>(), button => Equals(button.Content, "1x"));
        Assert.DoesNotContain(Descendants(headerStrip).OfType<Button>(), button => Equals(button.Content, "2x"));
        Assert.DoesNotContain(Descendants(headerStrip).OfType<Button>(), button => Equals(button.Content, "4x"));

        Assert.DoesNotContain(content.Children.OfType<Button>(), button => Grid.GetRow(button) == 2);
    }

    [Fact]
    public void BeatErrorDiagAlertDoesNotResizeThePlotRow()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateBeatErrorDiagContent();
        var contentSize = new Size(1280, 680);
        var tracePlot = Assert.IsType<AvaPlot>(content.Children.Single(child => Grid.GetRow(child) == 2));
        var headerStrip = Assert.IsType<Grid>(content.Children.Single(child => Grid.GetRow(child) == 0));
        Border banner = headerStrip.Children.OfType<Border>().Single();
        var alertText = Assert.IsType<TextBlock>(banner.Child);

        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));
        Rect hiddenBounds = tracePlot.Bounds;

        alertText.Text = "Tic/toc separation +0.90 ms exceeds the acceptable ±0.8 ms";
        banner.IsVisible = true;
        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));

        Assert.Equal(hiddenBounds, tracePlot.Bounds);
    }

    [Fact]
    public void BeatErrorDiagReadoutLabelsUseAccentBrush()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateBeatErrorDiagContent();
        var readoutGrid = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 1));
        StackPanel[] cells = readoutGrid.Children.OfType<StackPanel>().ToArray();

        Assert.Equal(BeatErrorReadout.Labels.Length, cells.Length);
        TextBlock[] labels = cells
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[0]))
            .ToArray();
        TextBlock[] values = cells
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[1]))
            .ToArray();

        Assert.Equal(BeatErrorReadout.Labels, labels.Select(label => label.Text).ToArray());
        Assert.All(labels, label =>
        {
            Assert.Equal(1.0, label.Opacity);
            Assert.True(label.IsSet(TextBlock.ForegroundProperty));
        });
        Assert.All(values, value =>
            Assert.False(value.IsSet(TextBlock.ForegroundProperty)));
    }

    [Fact]
    public void EscapementReadoutShowsFourCenteredStackedValues()
    {
        Grid content = CreateEscapementContent();
        var readoutGrid = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 1));
        StackPanel[] cells = readoutGrid.Children.OfType<StackPanel>().ToArray();
        TextBlock[] titles = cells
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[0]))
            .ToArray();
        TextBlock[] values = cells
            .Select(cell => Assert.IsType<TextBlock>(cell.Children[1]))
            .ToArray();

        Assert.Equal(4, readoutGrid.ColumnDefinitions.Count);
        Assert.Equal(4, cells.Length);
        Assert.Equal(EscapementReadout.Labels, titles.Select(title => title.Text).ToArray());
        Assert.DoesNotContain(titles, title => title.Text == "A→C Peak" || title.Text == "A→C Onset");
        Assert.All(cells, cell =>
        {
            Assert.Equal(Orientation.Vertical, cell.Orientation);
            Assert.Equal(HorizontalAlignment.Center, cell.HorizontalAlignment);
        });
        Assert.All(titles, title =>
        {
            Assert.Equal(FontWeight.Bold, title.FontWeight);
            Assert.Equal(14, title.FontSize);
            Assert.Equal(HorizontalAlignment.Center, title.HorizontalAlignment);
            Assert.IsAssignableFrom<IBrush>(title.GetValue(TextBlock.ForegroundProperty));
        });
        Assert.All(values, value =>
        {
            Assert.Equal(16, value.FontSize);
            Assert.Equal(HorizontalAlignment.Center, value.HorizontalAlignment);
            Assert.Equal(VarioReadout.Missing, value.Text);
        });
    }

    [Fact]
    public void TraceTabSmoothingButtonTogglesSplineOnBothPlots()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        InfoTabRegistration registration = registry.Registrations.Single(
            r => r.Definition.Id == InfoTabCatalog.TraceDisplayTabId);
        var content = Assert.IsType<Grid>(registration.TabItem.Content);

        // The consumer's Initialize builds the graphs (creates the scatters).
        registration.Consumer.Initialize(new AnalysisTabResetContext(48000, 10, 250));

        Button smoothing = Descendants(content)
            .OfType<Button>()
            .Single(button => Equals(button.Content, "Smoothing"));
        AvaPlot[] plots = Descendants(content).OfType<AvaPlot>().ToArray();
        Assert.Equal(2, plots.Length);

        // Scatter.Smooth is set-only; the spline state is observable through the
        // path strategy it drives (Straight -> CubicSpline).
        static string Strategy(AvaPlot plot) =>
            plot.Plot.GetPlottables<Scatter>().Single().PathStrategy.GetType().Name;

        Assert.All(plots, plot => Assert.Equal("CubicSpline", Strategy(plot)));
        Assert.Contains("active", smoothing.Classes);

        smoothing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.All(plots, plot => Assert.Equal("Straight", Strategy(plot)));
        Assert.DoesNotContain("active", smoothing.Classes);

        smoothing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.All(plots, plot => Assert.Equal("CubicSpline", Strategy(plot)));
        Assert.Contains("active", smoothing.Classes);
    }

    [Fact]
    public void LongTermTabOwnsReviewBarControls()
    {
        var vm = new MainWindowViewModel();
        Grid content = CreateLongTermContent(vm);
        content.DataContext = vm;

        Assert.Equal(5, content.RowDefinitions.Count);
        Border reviewBar = Assert.Single(content.Children.OfType<Border>(), border => border.Name == "ReviewBar");
        Assert.Equal(4, Grid.GetRow(reviewBar));
        Assert.True(reviewBar.IsVisible);
        Assert.False(reviewBar.IsEnabled);

        vm.SetPaused();
        Assert.True(vm.IsReviewBarEnabled);
        Assert.True(reviewBar.IsEnabled);

        string[] buttons = Descendants(reviewBar)
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "-1s", "+1s", "Live" }, buttons);
        Slider reviewSlider = Descendants(reviewBar).OfType<Slider>().Single(slider => slider.Name == "ReviewSlider");
        Assert.Equal(18.0, Assert.IsType<double>(reviewSlider.Resources["SliderHorizontalHeight"]));
        Assert.Equal(new GridLength(0), Assert.IsType<GridLength>(reviewSlider.Resources["SliderPreContentMargin"]));
        Assert.Equal(new GridLength(0), Assert.IsType<GridLength>(reviewSlider.Resources["SliderPostContentMargin"]));
        Assert.Contains(Descendants(reviewBar).OfType<TextBlock>(), text => text.Name == "ReviewReadoutLabel");
        Assert.Contains(Descendants(reviewBar).OfType<TextBlock>(), text => text.Name == "ReviewMetricsLabel");
    }

    [Fact]
    public void LongTermTabReportsReviewMetricsToViewModel()
    {
        var tabControl = new TabControl();
        var vm = new MainWindowViewModel();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial", vm);
        IAnalysisFrameConsumer consumer = registry.Consumers.Single(
            consumer => consumer.TabId == InfoTabCatalog.LongTermPerfTabId);

        consumer.Initialize(new AnalysisTabResetContext(48000, 10, 250));
        consumer.RenderFrame(
            new AnalysisFrame
            {
                MetricsHistory = new BeatMetricsHistorySnapshot
                {
                    Version = 1,
                    Rate = Series(new[] { 0.0, 10.0, 20.0 }, new[] { -1.0, -2.0, -3.0 }),
                    Amplitude = Series(new[] { 0.0, 10.0, 20.0 }, new[] { 280.0, 281.0, 282.0 }),
                    BeatError = Series(new[] { 0.0, 10.0, 20.0 }, new[] { 0.1, 0.2, 0.3 }),
                    RateValid = true,
                    RateSPerDay = -3.0,
                    AmplitudeValid = true,
                    AmplitudeDeg = 282.0,
                    BeatErrorValid = true,
                    BeatErrorSignedMs = 0.3,
                },
            },
            new AnalysisTabRenderContext(48000, ReviewCursorTimeS: 12.0));

        Assert.Equal("Cur. Error Rate -2.0 s/d   Cur. Amplitude 281°   Cur. Beat Error +0.2 ms", vm.ReviewMetricsText);
    }

    [Fact]
    public void VarioAcceptBandSpansAppearOnlyAfterMeasurementStartsWithoutTextBadges()
    {
        InfoTabRegistration registration = CreateVarioRegistration();
        Grid content = Assert.IsType<Grid>(registration.TabItem.Content);

        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text =>
            text.Text?.StartsWith("Acceptable band", StringComparison.Ordinal) == true);

        registration.Consumer.Initialize(new AnalysisTabResetContext(48000, 10, 250));

        HorizontalSpan[] spans = VarioAcceptBands(content);
        Assert.Equal(2, spans.Length);
        Assert.All(spans, span => Assert.False(span.IsVisible));

        registration.Consumer.RenderFrame(
            new AnalysisFrame
            {
                MetricsHistory = new BeatMetricsHistorySnapshot
                {
                    Version = 1,
                    RateValid = true,
                    RateSPerDay = 4.5,
                    AmplitudeValid = true,
                    AmplitudeDeg = 285.0,
                    RateStats = new StatsSummary(true, -2.0, 4.5, 1.1, 1.0, 10),
                    AmplitudeStats = new StatsSummary(true, 275.0, 285.0, 280.0, 2.0, 10),
                },
            },
            new AnalysisTabRenderContext(48000));

        Assert.All(spans, span => Assert.True(span.IsVisible));
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text =>
            text.Text?.StartsWith("Acceptable band", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void VarioStatsTableShowsRenderedStatsAboveTheGraphs()
    {
        InfoTabRegistration registration = CreateVarioRegistration();
        Grid content = Assert.IsType<Grid>(registration.TabItem.Content);

        registration.Consumer.Initialize(new AnalysisTabResetContext(48000, 10, 250));
        registration.Consumer.RenderFrame(
            new AnalysisFrame
            {
                MetricsHistory = new BeatMetricsHistorySnapshot
                {
                    Version = 1,
                    RateValid = true,
                    RateSPerDay = 2.7,
                    AmplitudeValid = true,
                    AmplitudeDeg = 208.0,
                    RateStats = new StatsSummary(true, -8.1, 6.3, 4.2, 1.65, 200),
                    AmplitudeStats = new StatsSummary(true, 192.0, 216.0, 203.0, 5.2, 200),
                },
            },
            new AnalysisTabRenderContext(48000));

        Assert.Equal(
            new[] { "-8.1 s/d", "+4.2 s/d", "+6.3 s/d", "1.65 s/d", "+2.7 s/d", "14.4 s/d" },
            StatsRowValues(content, row: 1));
        Assert.Equal(
            new[] { "192°", "203°", "216°", "5.20°", "208°", "24°" },
            StatsRowValues(content, row: 2));
    }

    [Fact]
    public void VarioOmitsManualLegendBelowPlots()
    {
        Grid content = CreateVarioContent();

        Assert.Equal(4, content.RowDefinitions.Count);
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text =>
            text.Text is "Blue solid" or "Red solid" or "Black short dash" ||
            text.Text?.Contains("= min/max", StringComparison.Ordinal) == true ||
            text.Text?.Contains("= average", StringComparison.Ordinal) == true ||
            text.Text?.Contains("= current", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void WatchHealthRailOmitsInlineCriteriaAndUsesBodyFontSize()
    {
        Grid content = CreateWatchHealthContent();

        TextBlock[] textBlocks = Descendants(content).OfType<TextBlock>().ToArray();
        Assert.DoesNotContain(textBlocks, text =>
            text.Text?.Contains("Criteria", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(textBlocks, text =>
            text.Text?.Contains("by position", StringComparison.OrdinalIgnoreCase) == true ||
            text.Text?.Contains("healthier", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(textBlocks, text =>
            text.Text?.Contains("Weakest", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(textBlocks, text => text.Text == "Review Focus: —");
        Assert.All(textBlocks, text => Assert.Equal(14, text.FontSize));
        Assert.All(Descendants(content).OfType<Button>(), button => Assert.Equal(14, button.FontSize));

        Border[] statusDots = Descendants(content)
            .OfType<Border>()
            .Where(border => border.Width == 10 && border.Height == 10)
            .ToArray();
        Assert.Equal(WatchHealthRadarModel.AxisOrder.Count, statusDots.Length);
        Assert.All(statusDots, dot => Assert.Equal("Worst status for this position", ToolTip.GetTip(dot)));
    }

    [Fact]
    public void WatchHealthTabRendersAtDefaultDpi()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateWatchHealthContent();
        var contentSize = new Size(1280, 680);
        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));

        var target = new RenderTargetBitmap(new PixelSize(1280, 680), new Vector(96, 96));
        target.Render(content);

        Assert.True(CountOpaquePixels(target, 1280, 680) > 10000);
    }

    [Fact]
    public void WatchHealthGuideScreenshotsExportAtDefaultDpi()
    {
        EnsureAvaloniaPlatform();
        string outputDirectory = Path.Combine(RepositoryRoot(), "artifacts", "health-guides");
        Directory.CreateDirectory(outputDirectory);

        double inBand = (VarioGaugePolicy.AmplitudeAcceptMinDeg + VarioGaugePolicy.AmplitudeAcceptMaxDeg) / 2.0;
        double serviceLow = VarioVerdict.AmplitudeServiceDeg - 20.0;

        CaptureGuide(
            "health-measuring.png",
            "Measuring…",
            WatchPosition.CH);
        CaptureGuide(
            "health-ok-in-range.png",
            "OK — In Range",
            WatchPosition.CH,
            WatchHealthRadarModel.AxisOrder
                .Select(position => Position(position, rate: 0.0, amplitude: inBand, beatError: 0.2, count: 60))
                .ToArray());
        CaptureGuide(
            "health-watch-review.png",
            "WATCH — Review",
            WatchPosition.CH,
            Position(WatchPosition.CH, rate: 0.0, amplitude: inBand, beatError: 0.2, count: 60),
            Position(WatchPosition.P3H, rate: 0.0, amplitude: inBand, beatError: 0.2, count: 60),
            Position(WatchPosition.P9H, rate: 20.0, amplitude: inBand, beatError: 0.2, count: 60));
        CaptureGuide(
            "health-alert-review-required.png",
            "ALERT — Review Required",
            WatchPosition.CB,
            Position(WatchPosition.CH, rate: 0.0, amplitude: inBand, beatError: 0.2, count: 60),
            Position(WatchPosition.CB, rate: 0.0, amplitude: serviceLow, beatError: 0.2, count: 60),
            Position(WatchPosition.P3H, rate: 0.0, amplitude: inBand, beatError: 0.2, count: 60));

        void CaptureGuide(string fileName, string expectedGuide, WatchPosition activePosition, params PositionSummary[] positions)
        {
            WatchHealthScreenshotSurface surface = CreateRenderedWatchHealthSurface(activePosition, positions);
            try
            {
                Assert.Contains(Descendants(surface.Content).OfType<TextBlock>(), text => text.Text == expectedGuide);

                string path = Path.Combine(outputDirectory, fileName);
                SaveControlScreenshot(surface.Content, path);

                Assert.True(File.Exists(path));
            }
            finally
            {
                surface.Window.Close();
            }
        }
    }

    private static Grid CreateVarioContent()
    {
        return Assert.IsType<Grid>(CreateVarioRegistration().TabItem.Content);
    }

    private static Grid CreateWatchHealthContent()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchHealthRadarTabId).TabItem.Content);
    }

    private static WatchHealthScreenshotSurface CreateRenderedWatchHealthSurface(
        WatchPosition activePosition,
        params PositionSummary[] positions)
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        InfoTabRegistration registration = registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchHealthRadarTabId);
        var content = Assert.IsType<Grid>(registration.TabItem.Content);
        registry.CreateRouter().Route(
            Frame(version: 1, activePosition, positions),
            InfoTabCatalog.WatchHealthRadarTabId,
            new AnalysisTabRenderContext(48000));

        tabControl.SelectedItem = registration.TabItem;
        var window = new Window
        {
            Width = 1280,
            Height = 680,
            Background = Brushes.White,
            Content = tabControl,
        };
        window.Show();

        return new WatchHealthScreenshotSurface(window, content);
    }

    private sealed record WatchHealthScreenshotSurface(Window Window, Grid Content);

    private static Grid CreateLongTermContent(MainWindowViewModel? viewModel = null)
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial", viewModel);
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.LongTermPerfTabId).TabItem.Content);
    }

    private static Grid CreateTraceContent()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.TraceDisplayTabId).TabItem.Content);
    }

    private static Grid CreateRateScopeContent()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.RateScopeTabId).TabItem.Content);
    }

    private static Grid CreateScopeSweepContent(MainWindowViewModel? viewModel = null)
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial", viewModel);
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.ScopeSweepTabId).TabItem.Content);
    }

    private static Grid CreateSpectrogramContent()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.SpectrogramTabId).TabItem.Content);
    }

    private static Grid CreateBeatNoiseContent(MainWindowViewModel? viewModel = null)
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial", viewModel);
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.BeatNoiseScopeTabId).TabItem.Content);
    }

    private static Grid CreateWaveformCompareContent()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WaveformCompareTabId).TabItem.Content);
    }

    private static Grid CreateMultiFilterScopeContent(MainWindowViewModel? viewModel = null)
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial", viewModel);
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.MultiFilterScopeTabId).TabItem.Content);
    }

    private static Grid CreateBeatErrorDiagContent()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.BeatErrorDiagTabId).TabItem.Content);
    }

    private static Grid CreateEscapementContent()
    {
        EnsureAvaloniaPlatform();
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.EscapementAnalyzerTabId).TabItem.Content);
    }

    private static InfoTabRegistration CreateVarioRegistration()
    {
        var tabControl = new TabControl();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, new Grid(), "Arial");
        return Assert.Single(
            registry.Registrations,
            registration => registration.Definition.Id == InfoTabCatalog.VarioTabId);
    }

    private static HorizontalSpan[] VarioAcceptBands(Control content)
    {
        return Descendants(content)
            .OfType<AvaPlot>()
            .SelectMany(plot => plot.Plot.GetPlottables<HorizontalSpan>())
            .ToArray();
    }

    private static Button[] ResetViewButtons(InfoTabRegistry registry)
    {
        return registry.Registrations
            .SelectMany(registration => Descendants(Assert.IsAssignableFrom<Control>(registration.TabItem.Content)))
            .OfType<Button>()
            .Where(button => Equals(button.Content, "Reset View"))
            .ToArray();
    }

    private static void AssertVisibleAlertBannerHeightMatchesButton(Grid content, string buttonContent)
    {
        EnsureAvaloniaPlatform();
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        Border banner = headerStrip.Children.OfType<Border>().Single();
        var alertText = Assert.IsType<TextBlock>(banner.Child);
        alertText.Text = "Tic/toc separation +0.90 ms exceeds the acceptable +-0.8 ms";
        banner.IsVisible = true;

        var contentSize = new Size(1280, 680);
        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));

        Button button = headerStrip.Children
            .OfType<StackPanel>()
            .Single()
            .Children
            .OfType<Button>()
            .First(button => Equals(button.Content, buttonContent));
        Assert.Equal(button.Bounds.Height, banner.Bounds.Height, 3);
    }

    private static MetricsHistorySeries Series(double[] x, double[] y) => new()
    {
        X = x,
        Y = y,
        YMin = y,
        YMax = y,
    };

    private static string[] StatsRowValues(Grid content, int row)
    {
        var statsBorder = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 1));
        var statsTable = Assert.IsType<Grid>(statsBorder.Child);
        return statsTable.Children
            .OfType<TextBlock>()
            .Where(text => Grid.GetRow(text) == row && Grid.GetColumn(text) > 0)
            .OrderBy(Grid.GetColumn)
            .Select(text => text.Text ?? string.Empty)
            .ToArray();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;

        if (control is Panel panel)
        {
            foreach (Control child in panel.Children.OfType<Control>())
            {
                foreach (Control descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        if (control is ContentControl { Content: Control content })
        {
            foreach (Control descendant in Descendants(content))
            {
                yield return descendant;
            }
        }

        if (control is Decorator { Child: Control childContent })
        {
            foreach (Control descendant in Descendants(childContent))
            {
                yield return descendant;
            }
        }
    }

    private static int CountOpaquePixels(RenderTargetBitmap bitmap, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        int opaque = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                opaque++;
            }
        }

        return opaque;
    }

    private static void SaveControlScreenshot(Control content, string path)
    {
        if (content is Panel panel)
        {
            panel.Background = Brushes.White;
        }

        var contentSize = new Size(1280, 680);
        content.Measure(contentSize);
        content.Arrange(new Rect(contentSize));

        var target = new RenderTargetBitmap(new PixelSize(1280, 680), new Vector(96, 96));
        target.Render(content);
        target.Save(path);

        Assert.True(CountOpaquePixels(target, 1280, 680) > 10000);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

}
