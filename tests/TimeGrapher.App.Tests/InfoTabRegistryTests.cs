using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
    private const double VarioCapturedMinimumFontSize = 16.0;
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
        Assert.All(
            measureColumns.Select(column => Assert.IsType<TextBlock>(column.Children[1])),
            status => Assert.True(status.FontSize >= 24));
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
            .Single(button => Equals(button.Content, "View criteria ▾"));
        var flyout = Assert.IsType<Flyout>(criteriaButton.Flyout);
        Assert.Equal(PlacementMode.BottomEdgeAlignedRight, flyout.Placement);
        var panel = Assert.IsType<StackPanel>(flyout.Content);
        Assert.True(panel.Width <= 360);
        TextBlock[] rules = panel.Children
            .OfType<TextBlock>()
            .Where(text => text.Text is { } value &&
                (value.StartsWith("Stable · Within Band:", StringComparison.Ordinal) ||
                 value.StartsWith("Within Band · unstable:", StringComparison.Ordinal) ||
                 value.StartsWith("Fast / Slow · out of range:", StringComparison.Ordinal) ||
                 value.StartsWith("Healthy:", StringComparison.Ordinal) ||
                 value.StartsWith("Slightly low / High:", StringComparison.Ordinal) ||
                 value.StartsWith("Low · service:", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(6, rules.Length);
        Assert.All(rules, rule =>
        {
            Assert.Equal(TextWrapping.Wrap, rule.TextWrapping);
            Assert.True(rule.MaxWidth <= 320);
        });
    }

    [Fact]
    public void VarioCriteriaGuideSitsBesideElapsedReadout()
    {
        Grid content = CreateVarioContent();
        var summaryCard = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        var summaryStack = Assert.IsType<StackPanel>(summaryCard.Child);
        var summaryColumns = Assert.IsType<Grid>(summaryStack.Children[1]);
        Button criteriaButton = Assert.IsType<Button>(
            summaryColumns.Children.Single(child => Grid.GetColumn(child) == 3));
        StackPanel elapsedColumn = Assert.IsType<StackPanel>(
            summaryColumns.Children.Single(child => Grid.GetColumn(child) == 2));

        Assert.Equal("View criteria ▾", criteriaButton.Content);
        Assert.True(criteriaButton.FontSize >= VarioCapturedMinimumFontSize);
        Assert.True(criteriaButton.MinWidth >= 168);
        Assert.True(criteriaButton.MinHeight >= 36);
        Assert.Equal(HorizontalAlignment.Left, criteriaButton.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, criteriaButton.VerticalAlignment);
        Assert.Equal(150, summaryColumns.ColumnDefinitions[2].Width.Value);
        Assert.Equal(HorizontalAlignment.Left, elapsedColumn.HorizontalAlignment);
        Assert.Contains(
            elapsedColumn.Children.OfType<TextBlock>(),
            text => text.Text == "Elapsed");
    }

    [Fact]
    public void VarioReadoutStripHeadersAreHighContrast()
    {
        Grid content = CreateVarioContent();
        Border[] readouts = content.Children
            .OfType<Border>()
            .Where(child => Grid.GetRow(child) is 2 or 5)
            .ToArray();
        TextBlock[] headers = readouts
            .Select(readout => Assert.IsType<Grid>(readout.Child))
            .SelectMany(strip => strip.Children
            .OfType<TextBlock>()
            .Where(text => Grid.GetRow(text) == 0))
            .ToArray();

        Assert.Equal(2, readouts.Length);
        Assert.Equal(12, headers.Length);
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
            .Single(button => Equals(button.Content, "View criteria ▾"));
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
    public void VarioGaugeHeadersUseBlankSpaceForAcceptBandBadges()
    {
        Grid content = CreateVarioContent();
        Grid rateHeader = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 1));
        Grid amplitudeHeader = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 4));

        Assert.Contains(
            Descendants(rateHeader).OfType<TextBlock>(),
            text => text.Text == "Acceptable band -4 to +6 s/d");
        Assert.Contains(
            Descendants(amplitudeHeader).OfType<TextBlock>(),
            text => text.Text == "Acceptable band 270 to 300°");
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
        Assert.Equal(4, content.RowDefinitions.Count);
        // Beat-error row (3) is enlarged to offset its visible time-axis so all three
        // data areas match; a revert to '*' would silently break equal heights.
        Assert.True(content.RowDefinitions[3].Height.IsStar);
        Assert.Equal(1.22, content.RowDefinitions[3].Height.Value, 3);
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

        Assert.Equal(5, content.RowDefinitions.Count);
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

        Assert.Equal(5, readoutGrid.ColumnDefinitions.Count);
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
    public void BeatErrorDiagTabReservesAlertBannerSpaceBesideZoomButtons()
    {
        EnsureAvaloniaPlatform();
        Grid content = CreateBeatErrorDiagContent();
        var headerStrip = Assert.IsType<Grid>(
            content.Children.Single(child => Grid.GetRow(child) == 0));

        Assert.Equal(2, headerStrip.ColumnDefinitions.Count);
        Assert.True(headerStrip.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(GridUnitType.Auto, headerStrip.ColumnDefinitions[1].Width.GridUnitType);

        Border banner = headerStrip.Children.OfType<Border>().Single();
        Assert.Equal(0, Grid.GetColumn(banner));
        Assert.Equal(TraceHeaderButtonMinHeightForTest, banner.MinHeight);
        Assert.False(banner.IsVisible);
        Assert.True(banner.Margin.Right > 0);

        StackPanel controls = headerStrip.Children.OfType<StackPanel>().Single();
        Assert.Equal(1, Grid.GetColumn(controls));
        Assert.All(controls.Children.OfType<Button>(), button =>
            Assert.Equal(TraceHeaderButtonMinHeightForTest, button.MinHeight));
        Assert.DoesNotContain(controls.Children.OfType<Button>(), button => Equals(button.Content, "Reset View"));
        string[] buttons = controls.Children
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "1x", "4x", "16x" }, buttons);
        Button defaultZoom = controls.Children.OfType<Button>().Single(button => Equals(button.Content, "1x"));
        Assert.Contains("PositionButton", defaultZoom.Classes);
        Assert.Equal(TraceHeaderButtonFontSizeForTest, defaultZoom.FontSize);
        Assert.Equal(TraceHeaderButtonMinHeightForTest, defaultZoom.MinHeight);
        Assert.Contains("active", defaultZoom.Classes);
        Assert.Equal(defaultZoom.MinHeight, banner.MinHeight);
        AssertVisibleAlertBannerHeightMatchesButton(content, "1x");

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
    public void LongTermTabOmitsReviewBarControls()
    {
        var vm = new MainWindowViewModel();
        Grid content = CreateLongTermContent(vm);
        content.DataContext = vm;

        Assert.Equal(4, content.RowDefinitions.Count);
        Assert.DoesNotContain(content.Children.OfType<Border>(), border => border.Name == "ReviewBar");
        Assert.DoesNotContain(Descendants(content).OfType<Button>(), button => Equals(button.Content, "-1s"));
        Assert.DoesNotContain(Descendants(content).OfType<Button>(), button => Equals(button.Content, "+1s"));
        Assert.DoesNotContain(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Live"));
        Assert.DoesNotContain(Descendants(content).OfType<Slider>(), slider => slider.Name == "ReviewSlider");
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
    public void VarioAcceptableBandBadgesAppearOnlyAfterMeasurementStarts()
    {
        InfoTabRegistration registration = CreateVarioRegistration();
        Grid content = Assert.IsType<Grid>(registration.TabItem.Content);
        Border rateBadge = AcceptBandBadge(content, "Acceptable band -4 to +6 s/d");
        Border amplitudeBadge = AcceptBandBadge(content, "Acceptable band 270 to 300°");

        Assert.False(rateBadge.IsVisible);
        Assert.False(amplitudeBadge.IsVisible);

        registration.Consumer.Initialize(new AnalysisTabResetContext(48000, 10, 250));

        Assert.False(rateBadge.IsVisible);
        Assert.False(amplitudeBadge.IsVisible);

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

        Assert.True(rateBadge.IsVisible);
        Assert.True(amplitudeBadge.IsVisible);
    }

    [Fact]
    public void VarioReadoutStripsShowRenderedStatsNearEachGauge()
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
            ReadoutValues(content, row: 2));
        Assert.Equal(
            new[] { "192°", "203°", "216°", "5.20°", "208°", "24°" },
            ReadoutValues(content, row: 5));
    }

    [Fact]
    public void VarioLegendNamesLineStylesForMarkers()
    {
        Grid content = CreateVarioContent();
        var legendBox = Assert.IsType<Viewbox>(
            content.Children.Single(child => Grid.GetRow(child) == 7));
        // The legend scales down only when too narrow, so every glyph stays visible.
        Assert.Equal(StretchDirection.DownOnly, legendBox.StretchDirection);
        var legend = Assert.IsType<TextBlock>(legendBox.Child);
        string legendText = string.Concat(legend.Inlines!.OfType<Run>().Select(run => run.Text));
        Run currentSwatch = legend.Inlines!.OfType<Run>()
            .Single(run => run.Text == "Black short dash");

        Assert.Equal(
            "Amber band = acceptable band   Blue solid = measured min/max   Red solid = average   Black short dash = current",
            legendText);
        Assert.IsAssignableFrom<IBrush>(currentSwatch.GetValue(TextElement.ForegroundProperty));
        Assert.Equal(TextWrapping.NoWrap, legend.TextWrapping);
    }

    private static Grid CreateVarioContent()
    {
        return Assert.IsType<Grid>(CreateVarioRegistration().TabItem.Content);
    }

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

    private static Border AcceptBandBadge(Control content, string text)
    {
        return Descendants(content)
            .OfType<Border>()
            .Single(border => border.Child is TextBlock { Text: var value } && value == text);
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

    private static string[] ReadoutValues(Grid content, int row)
    {
        var readout = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == row));
        var strip = Assert.IsType<Grid>(readout.Child);
        return strip.Children
            .OfType<TextBlock>()
            .Where(text => Grid.GetRow(text) == 1)
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

}
