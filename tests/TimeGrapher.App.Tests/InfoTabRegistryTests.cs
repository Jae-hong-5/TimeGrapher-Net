using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ScottPlot.Avalonia;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class InfoTabRegistryTests
{
    private const double VarioCapturedMinimumFontSize = 16.0;

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

        Assert.Equal(6, buttons.Length);
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
        WatchPositionDiagram[] diagrams = Descendants(content).OfType<WatchPositionDiagram>().ToArray();
        WatchPositionDiagram activeDiagram = Assert.Single(diagrams);

        Assert.Equal(WatchPosition.CH, activeDiagram.Position);
        Assert.False(activeDiagram.ShowLabels);
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text => text.Text == "POSITION MAP");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "Amplitude");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "POSITION CONSISTENCY");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "METRIC");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "STATUS");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "NEED POSITION");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "READY POSITION");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "READING");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "BALANCE-WHEEL");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "VERT SPREAD");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "COLLECTING");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "REFERENCE");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "CH: 0/30 beats. Keep measuring this position.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "3 positions, 30+ beats each");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "2 full vertical positions, 30+ beats each");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "1 full vertical + 1 horizontal");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "any measured position");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "None (0/3)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "None (0/2)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "V None / H None (0V + 0H)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "None");
        Assert.Contains(Descendants(content).OfType<Button>(), button => Equals(button.Content, "View criteria ▾"));
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "Worst - best across positions.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "Vertical mean - horizontal mean.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "Mean of measured positions.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "Verdict starts at 3 positions with 30+ beats. Later qualified positions update the result.");
        Assert.All(
            Descendants(content).OfType<TextBlock>().Where(PositionTextHasLocalFontSize),
            text => Assert.True(text.FontSize >= 14.0, $"{text.Text} uses {text.FontSize}px"));
        Grid tableGrid = Assert.Single(Descendants(content).OfType<Grid>(), grid =>
            grid.ColumnDefinitions.Count == 5 &&
            grid.Children.OfType<TextBlock>().Any(text => text.Text == "POS"));
        Assert.Equal(WatchPositions.Count + 1, tableGrid.RowDefinitions.Count);
        Assert.DoesNotContain(Descendants(content).OfType<Border>(), border =>
            border.Classes.Contains("PositionMapTile"));
        Assert.Contains(Descendants(content).OfType<Border>(), border => border.Classes.Contains("PositionResultPanel"));
        Assert.Contains(Descendants(content).OfType<Border>(), border =>
            border.Classes.Contains("PositionResultBadge") &&
            border.Classes.Contains("pending"));
        Border[] summaryGroups = Descendants(content)
            .OfType<Border>()
            .Where(border => border.Classes.Contains("PositionResultGroup"))
            .ToArray();
        Assert.Equal(4, summaryGroups.Length);
        Assert.Equal(3, summaryGroups.Count(group => group.Classes.Contains("primary")));
        Assert.All(summaryGroups, group =>
        {
            Assert.False(group.ClipToBounds);
            Assert.True(double.IsNaN(group.Height));
            TextBlock[] groupText = Descendants(group).OfType<TextBlock>().ToArray();
            Assert.Contains(groupText, text => text.TextWrapping == TextWrapping.Wrap);
            Assert.All(groupText.Where(text => text.Text is { Length: > 24 }), text =>
                Assert.Equal(TextWrapping.Wrap, text.TextWrapping));
        });
        for (int i = 0; i < buttons.Length; i++)
        {
            Assert.Equal(i, Grid.GetRow(buttons[i]));
            Assert.Equal(VerticalAlignment.Stretch, buttons[i].VerticalAlignment);
        }
        Assert.Single(registry.Consumers, consumer => consumer.TabId == InfoTabCatalog.WatchPositionsTabId);
    }

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
        WatchPositionDiagram diagram = Assert.Single(Descendants(
            Assert.IsType<Grid>(registry.Registrations.Single(
                registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content))
            .OfType<WatchPositionDiagram>());
        Assert.Equal(WatchPosition.P6H, diagram.Position);
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

        WatchPositionDiagram diagram = Assert.Single(Descendants(content).OfType<WatchPositionDiagram>());
        Assert.Equal(WatchPosition.P9H45, diagram.Position);
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "7:30H");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "7:30 up");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "7:30H: 0/30 beats. Keep measuring this position.");
        Border activeRow = Assert.Single(Descendants(content).OfType<Border>(),
            border => border.Classes.Contains("SeqActiveRow"));
        Assert.Equal(8, Grid.GetRow(activeRow));
    }

    [Fact]
    public void PositionCriteriaFlyoutExplainsDiagnosticBasisAndMeaning()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        var content = Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content);
        Button criteriaButton = Descendants(content)
            .OfType<Button>()
            .Single(button => Equals(button.Content, "View criteria ▾"));

        Assert.True(criteriaButton.FontSize >= 14.0);
        Assert.True(criteriaButton.MinWidth >= 148);
        Assert.True(criteriaButton.MinHeight >= 32);
        var flyout = Assert.IsType<Flyout>(criteriaButton.Flyout);
        Assert.Equal(PlacementMode.BottomEdgeAlignedRight, flyout.Placement);
        var panel = Assert.IsType<StackPanel>(flyout.Content);
        Assert.True(panel.Width <= 380);
        TextBlock[] textBlocks = panel.Children.OfType<TextBlock>().ToArray();

        Assert.Contains(textBlocks, text => text.Text == "Position criteria");
        Assert.Contains(textBlocks, text => text.Text == "D Spread");
        Assert.Contains(textBlocks, text => text.Text == "Balance-wheel");
        Assert.Contains(textBlocks, text => text.Text == "V/H Balance");
        Assert.Contains(textBlocks, text =>
            text.Text == "Meaning: high positional variation. It does not identify the mechanical cause by itself.");
        Assert.Contains(textBlocks, text =>
            text.Text == "Meaning: possible balance-wheel centering or balancing issue.");
        Assert.Contains(textBlocks, text =>
            text.Text == "Meaning: vertical-vs-horizontal bias. Treat it separately from balance-wheel unbalance.");
        Assert.All(textBlocks.Where(text => text.TextWrapping == TextWrapping.Wrap), text =>
        {
            Assert.True(text.MaxWidth <= 340);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        });
    }

    [Fact]
    public void PositionResultPanelUpdatesConsistencyVerdictFromSequenceSpread()
    {
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
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 117)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "1/3 positions ready. Measure another position to 30 beats.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "CH (1/3)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "None (0/2)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "V None / H CH (0V + 1H)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "CH");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("pending"));

        router.Route(
            Frame(
                version: 2,
                activePosition: WatchPosition.P6H,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.P6H, rate: 2.0, amplitude: 301.0, count: 30)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "2/3 positions ready. Measure another position to 30 beats.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "CH, 6H (2/3)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "6H (1/2)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "V 6H / H CH (1V + 1H)");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("pending"));

        router.Route(
            Frame(
                version: 3,
                activePosition: WatchPosition.P12H,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.P6H, rate: 2.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P3H, rate: 1.0, amplitude: 301.0, count: 30)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "COLLECTING");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "12H: 0/30 beats. Keep measuring this position.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "READY");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "CH, 6H, 3H (3/3)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "6H, 3H (2/2)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "V 6H, 3H / H CH (2V + 1H)");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("pending"));

        router.Route(
            Frame(
                version: 4,
                activePosition: WatchPosition.P12H,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.P6H, rate: 2.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P3H, rate: 1.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P12H, rate: 3.0, amplitude: 301.0, count: 5)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "12H: 5/30 beats. Keep measuring this position.");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("pending"));

        router.Route(
            Frame(
                version: 5,
                activePosition: WatchPosition.P12H,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.P6H, rate: 2.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P3H, rate: 1.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P12H, rate: 3.0, amplitude: 301.0, count: 30)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "OK");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "REFERENCE");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "4 positions ready. Spread is within 15 s/d.");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "CH, 6H, 3H, 12H (4/3)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "6H, 3H, 12H (3/2)");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "V 6H, 3H, 12H / H CH (3V + 1H)");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("ok"));

        router.Route(
            Frame(
                version: 6,
                activePosition: WatchPosition.P12H,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.P6H, rate: 2.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P3H, rate: 1.0, amplitude: 301.0, count: 30),
                Position(WatchPosition.P12H, rate: 30.0, amplitude: 301.0, count: 30)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "CHECK");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "4 positions ready. Spread is above 15 s/d.");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("warn"));
    }

    [Fact]
    public void PositionResultPanelRequiresVerticalAndHorizontalPositionsBeforeOk()
    {
        var tabControl = new TabControl();
        var positionStrip = new Grid();
        InfoTabRegistry registry = InfoTabRegistry.FromCatalog(tabControl, positionStrip, "Arial");
        var content = Assert.IsType<Grid>(registry.Registrations.Single(
            registration => registration.Definition.Id == InfoTabCatalog.WatchPositionsTabId).TabItem.Content);
        AnalysisFrameRouter router = registry.CreateRouter();

        // Three qualified positions with the active one settled, but only one is
        // vertical: the balance-wheel requirement the guide advertises (>=2
        // vertical, 1 horizontal) is unmet, so the verdict stays COLLECTING
        // instead of reporting OK on an all-but-one-horizontal set.
        router.Route(
            Frame(
                version: 1,
                activePosition: WatchPosition.P6H,
                Position(WatchPosition.CH, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.CB, rate: 0.0, amplitude: 300.0, count: 30),
                Position(WatchPosition.P6H, rate: 2.0, amplitude: 301.0, count: 30)),
            InfoTabCatalog.WatchPositionsTabId,
            new AnalysisTabRenderContext(48000));

        Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "COLLECTING");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "Need Position: 2 full vertical + 1 horizontal. Ready Position: V 6H / H CH, CB (1V + 2H).");
        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
            text.Text == "V 6H / H CH, CB (1V + 2H)");
        Assert.Contains(ResultBadges(content), badge => badge.Classes.Contains("pending"));
    }

    [Fact]
    public void VarioSummaryShowsVerdictsWithoutNumericSublines()
    {
        Grid content = CreateVarioContent();
        var summaryCard = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        var summaryStack = Assert.IsType<StackPanel>(summaryCard.Child);
        var summaryTopBar = Assert.IsType<Grid>(summaryStack.Children[0]);
        var overallText = Assert.IsType<TextBlock>(
            summaryTopBar.Children.Single(child => Grid.GetColumn(child) == 0));
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
                (value.StartsWith("Stable · in range:", StringComparison.Ordinal) ||
                 value.StartsWith("In range · unstable:", StringComparison.Ordinal) ||
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
    public void VarioCriteriaGuideSitsAboveElapsedReadout()
    {
        Grid content = CreateVarioContent();
        var summaryCard = Assert.IsType<Border>(
            content.Children.Single(child => Grid.GetRow(child) == 0));
        var summaryStack = Assert.IsType<StackPanel>(summaryCard.Child);
        var summaryTopBar = Assert.IsType<Grid>(summaryStack.Children[0]);
        var summaryColumns = Assert.IsType<Grid>(summaryStack.Children[1]);
        Button criteriaButton = Assert.IsType<Button>(
            summaryTopBar.Children.Single(child => Grid.GetColumn(child) == 1));
        StackPanel elapsedColumn = Assert.IsType<StackPanel>(
            summaryColumns.Children.Single(child => Grid.GetColumn(child) == 2));

        Assert.Equal("View criteria ▾", criteriaButton.Content);
        Assert.True(criteriaButton.FontSize >= VarioCapturedMinimumFontSize);
        Assert.True(criteriaButton.MinWidth >= 168);
        Assert.True(criteriaButton.MinHeight >= 36);
        Assert.Equal(HorizontalAlignment.Right, criteriaButton.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Top, criteriaButton.VerticalAlignment);
        Assert.Equal(160, summaryColumns.ColumnDefinitions[2].Width.Value);
        Assert.Equal(HorizontalAlignment.Left, elapsedColumn.HorizontalAlignment);
        Assert.Contains(
            elapsedColumn.Children.OfType<TextBlock>(),
            text => text.Text == "ELAPSED");
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
            text => text.Text == "Acceptable band -10 to +10 s/d");
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

        Assert.Equal(new[] { "1h", "3h", "6h", "‹", "›" }, buttons);
        Assert.DoesNotContain(Descendants(header).OfType<TextBlock>(), text => text.Text == "24H LONG-TERM");
        Assert.Contains(Descendants(header).OfType<TextBlock>(), text => text.Text == "COLLECTING");
        Assert.Contains(Descendants(header).OfType<TextBlock>(), text => text.Text == "Error Rate —");
        Assert.DoesNotContain(Descendants(header).OfType<Button>(), button => button.Classes.Contains("active"));
        Assert.DoesNotContain(Descendants(header).OfType<TextBlock>(), text => text.Text?.Contains("Elapsed", StringComparison.Ordinal) == true);
        Assert.Empty(header.RowDefinitions);
        Assert.Equal(6, content.RowDefinitions.Count);
        Assert.Equal(new Thickness(0, 0, 0, -8), Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 1)).Margin);
        Assert.Equal(new Thickness(0, -8, 0, -8), Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 2)).Margin);
        Assert.Equal(new Thickness(0, -8, 0, 0), Assert.IsType<AvaPlot>(
            content.Children.Single(child => Grid.GetRow(child) == 3)).Margin);
        Assert.DoesNotContain(Descendants(content).OfType<TextBlock>(), text =>
            text.Text?.StartsWith("Shaded band =", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void LongTermTabOwnsReviewBarControls()
    {
        var vm = new MainWindowViewModel(() => Task.CompletedTask, () => { }, () => { });
        Grid content = CreateLongTermContent(vm);
        content.DataContext = vm;

        Border reviewBar = Assert.Single(content.Children.OfType<Border>(), border => border.Name == "ReviewBar");
        Assert.Equal(5, Grid.GetRow(reviewBar));
        Assert.True(reviewBar.IsVisible);
        Assert.False(reviewBar.IsEnabled);

        vm.SetRunning();
        Assert.False(reviewBar.IsEnabled);

        vm.SetPaused();
        Assert.True(reviewBar.IsEnabled);

        string[] buttons = Descendants(reviewBar)
            .OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "-1 s", "+1 s", "LIVE" }, buttons);
        Assert.Contains(Descendants(reviewBar).OfType<Slider>(), slider => slider.Name == "ReviewSlider");
        Assert.Contains(Descendants(reviewBar).OfType<TextBlock>(), text => text.Name == "ReviewReadoutLabel");
        Assert.Contains(Descendants(reviewBar).OfType<TextBlock>(), text => text.Name == "ReviewMetricsLabel");
    }

    [Fact]
    public void LongTermTabReportsReviewMetricsToViewModel()
    {
        var tabControl = new TabControl();
        var vm = new MainWindowViewModel(() => Task.CompletedTask, () => { }, () => { });
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

        Assert.Equal("Error Rate -2.0 s/d   Amplitude 281°   BEAT ERROR +0.2 ms", vm.ReviewMetricsText);
    }

    [Fact]
    public void VarioAcceptableBandBadgesAppearOnlyAfterMeasurementStarts()
    {
        InfoTabRegistration registration = CreateVarioRegistration();
        Grid content = Assert.IsType<Grid>(registration.TabItem.Content);
        Border rateBadge = AcceptBandBadge(content, "Acceptable band -10 to +10 s/d");
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
            .Single(run => run.Text == "Red dashed");

        Assert.Equal(
            "Amber band = acceptable band   Blue solid = measured min/max   Red solid = average   Red dashed = current",
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

    private static IEnumerable<Border> ResultBadges(Control content)
    {
        return Descendants(content)
            .OfType<Border>()
            .Where(border => border.Classes.Contains("PositionResultBadge"));
    }

    private static Button[] ResetViewButtons(InfoTabRegistry registry)
    {
        return registry.Registrations
            .SelectMany(registration => Descendants(Assert.IsAssignableFrom<Control>(registration.TabItem.Content)))
            .OfType<Button>()
            .Where(button => Equals(button.Content, "Reset View"))
            .ToArray();
    }

    private static bool PositionTextHasLocalFontSize(TextBlock text)
    {
        return !string.IsNullOrWhiteSpace(text.Text) &&
            text.FontSize > 0.0;
    }

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

    private static StatsSummary Stats(double mean, long count = 10) =>
        new(Valid: true, Min: mean, Max: mean, Mean: mean, Sigma: 0.0, Count: count);

    private static MetricsHistorySeries Series(double[] x, double[] y) => new()
    {
        X = x,
        Y = y,
        YMin = y,
        YMax = y,
    };

    private static PositionSummary Position(
        WatchPosition position,
        double? rate = null,
        double? amplitude = null,
        double? beatError = null,
        long count = 10) => new(
        position,
        rate is double r ? Stats(r, count) : default,
        amplitude is double a ? Stats(a, count / 2) : default,
        beatError is double b ? Stats(b, count) : default);

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
