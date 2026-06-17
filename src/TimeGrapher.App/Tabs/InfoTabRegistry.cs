using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScottPlot.Avalonia;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Tabs;

internal sealed record InfoTabRegistration(
    InfoTabDefinition Definition,
    TabItem TabItem,
    IAnalysisFrameConsumer Consumer);

internal sealed partial class InfoTabRegistry
{
    private const double VarioMinimumFontSize = 16.0;
    private const double PositionMinimumFontSize = 14.0;

    private delegate InfoTabRegistration InfoTabFactory(
        InfoTabDefinition definition,
        InfoTabFactoryContext context);

    private sealed class InfoTabFactoryContext
    {
        public required string TextFontFamily { get; init; }
        public required Grid PositionButtonGrid { get; init; }
        public MainWindowViewModel? ViewModel { get; init; }
        public Image? SoundImageControl { get; set; }
    }

    private static readonly IReadOnlyDictionary<InfoTabKind, InfoTabFactory> Factories =
        new Dictionary<InfoTabKind, InfoTabFactory>
        {
            [InfoTabKind.RateScope] = CreateRateScopeRegistration,
            [InfoTabKind.SoundPrint] = CreateSoundPrintRegistration,
            [InfoTabKind.TraceDisplay] = CreateTraceDisplayRegistration,
            [InfoTabKind.ScopeSweep] = CreateScopeSweepRegistration,
            [InfoTabKind.Vario] = CreateVarioRegistration,
            [InfoTabKind.BeatErrorDiag] = CreateBeatErrorDiagRegistration,
            [InfoTabKind.MultiFilterScope] = CreateMultiFilterScopeRegistration,
            [InfoTabKind.LongTermPerformance] = CreateLongTermPerfRegistration,
            [InfoTabKind.TestPositions] = CreateTestPositionsRegistration,
            [InfoTabKind.BeatNoiseScope] = CreateBeatNoiseScopeRegistration,
            [InfoTabKind.EscapementAnalyzer] = CreateEscapementAnalyzerRegistration,
            [InfoTabKind.WaveformCompare] = CreateWaveformCompareRegistration,
            [InfoTabKind.Spectrogram] = CreateSpectrogramRegistration,
        };

    private readonly IReadOnlyList<InfoTabRegistration> _registrations;
    private readonly IAnalysisFrameConsumer[] _consumers;

    private InfoTabRegistry(IReadOnlyList<InfoTabRegistration> registrations, Image? soundImageControl)
    {
        _registrations = registrations;
        _consumers = registrations.Select(registration => registration.Consumer).ToArray();
        SoundImageControl = soundImageControl;
    }

    public IReadOnlyList<InfoTabRegistration> Registrations => _registrations;
    public IReadOnlyList<IAnalysisFrameConsumer> Consumers => _consumers;
    public Image? SoundImageControl { get; }

    public static InfoTabRegistry FromCatalog(
        TabControl tabControl,
        Grid positionButtonGrid,
        string textFontFamily,
        MainWindowViewModel? viewModel = null)
    {
        tabControl.Items.Clear();
        var registrations = new List<InfoTabRegistration>(InfoTabCatalog.All.Count);
        var context = new InfoTabFactoryContext
        {
            TextFontFamily = textFontFamily,
            PositionButtonGrid = positionButtonGrid,
            ViewModel = viewModel,
        };

        foreach (InfoTabDefinition definition in InfoTabCatalog.All)
        {
            InfoTabRegistration registration = CreateRegistration(definition, context);
            tabControl.Items.Add(registration.TabItem);
            registrations.Add(registration);
        }

        if (tabControl.SelectedIndex < 0 && tabControl.ItemCount > 0)
        {
            tabControl.SelectedIndex = 0;
        }

        return new InfoTabRegistry(registrations, context.SoundImageControl);
    }

    public AnalysisFrameRouter CreateRouter()
    {
        return new AnalysisFrameRouter(_registrations.Select(registration => registration.Consumer));
    }

    /// <summary>
    /// Small overlay-chrome button (the shared styling of the per-plot
    /// "Reset View" buttons and toolbar selectors). Position it at the call
    /// site (alignment / margin / grid row).
    /// </summary>
    private static Button CreateOverlayButton(string content, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Pins an overlay button to the top-right corner of a plot grid row.</summary>
    private static Button CreatePinnedResetViewButton(string tooltip, int row, Action onClick)
    {
        Button button = CreateOverlayButton("Reset View", tooltip, onClick);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0, 6, 10, 0);
        Grid.SetRow(button, row);
        return button;
    }

    /// <summary>
    /// Accent alert banner shared by the alerting tabs (hidden until the
    /// renderer sets a message); the background binds to the theme accent so
    /// it recolors with the chrome.
    /// </summary>
    private static Border CreateAlertBanner(out TextBlock alertText)
    {
        var text = new TextBlock
        {
            Foreground = Avalonia.Media.Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var banner = new Border
        {
            Padding = new Thickness(8, 3),
            IsVisible = false,
            Child = text,
        };
        banner.Bind(
            Border.BackgroundProperty,
            banner.GetResourceObservable("ChromeAccentBrush"));
        alertText = text;
        return banner;
    }

    private static InfoTabRegistration CreateRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        if (!Factories.TryGetValue(definition.Kind, out InfoTabFactory? factory))
        {
            throw new InvalidOperationException($"Unsupported info tab kind '{definition.Kind}'.");
        }

        return factory(definition, context);
    }

    private static InfoTabRegistration CreateRateScopeRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var ratePlot = new AvaPlot();
        var scopePlot = new AvaPlot();
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
        };
        Grid.SetRow(ratePlot, 0);
        Grid.SetRow(scopePlot, 1);
        grid.Children.Add(ratePlot);
        grid.Children.Add(scopePlot);

        // "Waiting for beat sync" overlay sits over the rate-error plot (the scope
        // below already shows the live waveform before sync).
        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 0);
            grid.Children.Add(overlay);
        }

        var renderer = new RateScopeRenderer(scopePlot, ratePlot, context.TextFontFamily);

        grid.Children.Add(CreatePinnedResetViewButton("Reset this graph's view", row: 0, renderer.ResetRateView));
        grid.Children.Add(CreatePinnedResetViewButton("Reset this graph's view", row: 1, renderer.ResetScopeView));

        var consumer = new RateScopeFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateSoundPrintRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var image = new Image
        {
            Stretch = Stretch.Fill,
        };
        var grid = new Grid();
        grid.Children.Add(image);
        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            grid.Children.Add(overlay);
        }
        context.SoundImageControl = image;

        var renderer = new SoundPrintRenderer(image);
        var consumer = new SoundPrintFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateTraceDisplayRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();

        Border alertBanner = CreateAlertBanner(out TextBlock alertText);


        var summaryText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 2),
        };
        var explanationText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.65,
            Margin = new Thickness(8, 0, 8, 3),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Rate: above 0 = gaining, below 0 = losing; flat = stable. " +
                   "Amplitude: shaded band marks the healthy 270–300° range.",
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,*,Auto,Auto"),
        };
        Grid.SetRow(alertBanner, 0);
        Grid.SetRow(ratePlot, 1);
        Grid.SetRow(amplitudePlot, 2);
        Grid.SetRow(summaryText, 3);
        Grid.SetRow(explanationText, 4);
        grid.Children.Add(alertBanner);
        grid.Children.Add(ratePlot);
        grid.Children.Add(amplitudePlot);
        grid.Children.Add(summaryText);
        grid.Children.Add(explanationText);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var renderer = new TraceDisplayRenderer(ratePlot, amplitudePlot, alertBanner, alertText, summaryText);

        grid.Children.Add(CreatePinnedResetViewButton("Re-enable live auto-scaling on both graphs", row: 1, renderer.ResetView));

        var consumer = new TraceDisplayFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateScopeSweepRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var sweepPlot = new AvaPlot();

        // Compact reference line of the most recent measurements under the plot
        // (the plan's "compare the live waveform against the most recent
        // timing test" readings).
        var referenceText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 2),
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetRow(sweepPlot, 0);
        Grid.SetRow(referenceText, 1);
        grid.Children.Add(sweepPlot);
        grid.Children.Add(referenceText);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 0);
            grid.Children.Add(overlay);
        }

        // 1x/2x/3x sweep-time selector pinned to the top-right of the plot. The
        // buttons write the shared SweepMultiple view-model property; MainWindow
        // forwards the change to the running analysis worker (the
        // SetSoundBackgroundColor flow). The active multiple renders disabled.
        if (context.ViewModel is { } viewModel)
        {
            int[] multiples = { 1, 2, 3 };
            var buttons = new Button[multiples.Length];

            void UpdateButtonStates()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].IsEnabled = multiples[i] != viewModel.SweepMultiple;
                }
            }

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 10, 0),
            };
            for (int i = 0; i < multiples.Length; i++)
            {
                int multiple = multiples[i];
                var button = new Button
                {
                    Content = multiple + "x",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 11,
                };
                ToolTip.SetTip(button, $"Sweep window = {multiple}x the tick-tick interval");
                button.Click += (_, _) =>
                {
                    viewModel.SweepMultiple = multiple;
                    UpdateButtonStates();
                };
                buttons[i] = button;
                buttonRow.Children.Add(button);
            }

            UpdateButtonStates();
            Grid.SetRow(buttonRow, 0);
            grid.Children.Add(buttonRow);
        }

        var renderer = new ScopeSweepRenderer(sweepPlot, referenceText);
        // Reset View sits top-left so it never collides with the 1x/2x/3x
        // selector pinned top-right.
        Button resetView = CreateOverlayButton(
            "Reset View", "Re-enable live auto-fitting of the sweep window", renderer.ResetView);
        resetView.HorizontalAlignment = HorizontalAlignment.Left;
        resetView.VerticalAlignment = VerticalAlignment.Top;
        resetView.Margin = new Thickness(10, 6, 0, 0);
        Grid.SetRow(resetView, 0);
        grid.Children.Add(resetView);
        var consumer = new ScopeSweepFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateVarioRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var font = new FontFamily(context.TextFontFamily);

        Grid GaugeHeader(string text, string bandText, out Border bandBadge)
        {
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                Margin = new Thickness(8, 2, 8, 0),
            };
            var title = new TextBlock
            {
                Text = text,
                FontSize = VarioMinimumFontSize,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bandBadge = new Border
            {
                BorderThickness = new Thickness(1),
                IsVisible = false,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(10, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = bandText,
                    FontSize = VarioMinimumFontSize,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            bandBadge.Bind(Border.BackgroundProperty, bandBadge.GetResourceObservable("VarioAcceptBandBadgeBrush"));
            bandBadge.Bind(Border.BorderBrushProperty, bandBadge.GetResourceObservable("VarioAcceptBandEdgeBrush"));
            Grid.SetColumn(title, 0);
            Grid.SetColumn(bandBadge, 1);
            header.Children.Add(title);
            header.Children.Add(bandBadge);
            return header;
        }

        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();

        var rateStatus = new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold };
        var ampStatus = new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold };
        var elapsedValue = new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold, FontFamily = font };

        StackPanel SummaryColumn(string caption, TextBlock status)
        {
            var sp = new StackPanel { Margin = new Thickness(12, 0, 12, 2) };
            sp.Children.Add(new TextBlock { Text = caption, FontSize = VarioMinimumFontSize, Opacity = 0.9, FontWeight = FontWeight.SemiBold });
            sp.Children.Add(status);
            return sp;
        }

        var elapsedColumn = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(12, 0, 12, 2),
        };
        elapsedColumn.Children.Add(new TextBlock { Text = "ELAPSED", FontSize = VarioMinimumFontSize, Opacity = 0.9, FontWeight = FontWeight.SemiBold });
        elapsedColumn.Children.Add(elapsedValue);

        var criteriaButton = new Button
        {
            Content = "View criteria ▾",
            FontSize = VarioMinimumFontSize,
            MinWidth = 168,
            MinHeight = 36,
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 4, 8, 0),
            Flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                Content = BuildVarioCriteria(),
            },
        };

        var summaryColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,160") };
        Control[] summaryCells =
        {
            SummaryColumn("RATE", rateStatus),
            SummaryColumn("AMPLITUDE", ampStatus),
            elapsedColumn,
        };
        for (int c = 0; c < summaryCells.Length; c++)
        {
            Grid.SetColumn(summaryCells[c], c);
            summaryColumns.Children.Add(summaryCells[c]);
        }

        var overallText = new TextBlock
        {
            FontSize = VarioMinimumFontSize,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 22,
            Margin = new Thickness(12, 5, 12, 0),
            Text = " ",
        };

        var summaryTopBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(overallText, 0);
        Grid.SetColumn(criteriaButton, 1);
        summaryTopBar.Children.Add(overallText);
        summaryTopBar.Children.Add(criteriaButton);

        var summaryStack = new StackPanel();
        summaryStack.Children.Add(summaryTopBar);
        summaryStack.Children.Add(summaryColumns);
        var summaryCard = new Border
        {
            Child = summaryStack,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16, 6, 16, 3),
            Padding = new Thickness(0, 0, 0, 4),
        };
        summaryCard.Bind(Border.BackgroundProperty, summaryCard.GetResourceObservable("PanelBgBrush"));
        summaryCard.Bind(Border.BorderBrushProperty, summaryCard.GetResourceObservable("ChromeBorderBrush"));

        string[] columnHeaders = { "Min", "Max", "Max−Min", "Average", "Std dev (σ)", "Current" };
        string?[] columnBrushKeys =
        {
            "VarioMinMaxBrush", "VarioMinMaxBrush", null,
            "VarioAverageBrush", null, null,
        };
        int[] stripOrder =
        {
            VarioRenderer.CellMin,
            VarioRenderer.CellAverage,
            VarioRenderer.CellMax,
            VarioRenderer.CellSigma,
            VarioRenderer.CellCurrent,
            VarioRenderer.CellSpread,
        };

        TextBlock[] BuildCells()
        {
            var cells = new TextBlock[VarioRenderer.CellCount];
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = VarioReadout.Missing,
                    FontFamily = font,
                    FontSize = VarioMinimumFontSize,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                if (columnBrushKeys[i] is string brushKey)
                {
                    cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable(brushKey));
                }

                cells[i] = cell;
            }

            return cells;
        }

        TextBlock[] rateCells = BuildCells();
        TextBlock[] amplitudeCells = BuildCells();

        Border BuildReadoutStrip(TextBlock[] cells)
        {
            var strip = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            for (int displayColumn = 0; displayColumn < stripOrder.Length; displayColumn++)
            {
                int cellIndex = stripOrder[displayColumn];
                var header = new TextBlock
                {
                    Text = columnHeaders[cellIndex],
                    FontSize = VarioMinimumFontSize,
                    Opacity = 0.82,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 10, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                cells[cellIndex].Margin = new Thickness(0, 0, 10, 0);
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, displayColumn);
                Grid.SetRow(cells[cellIndex], 1);
                Grid.SetColumn(cells[cellIndex], displayColumn);
                strip.Children.Add(header);
                strip.Children.Add(cells[cellIndex]);
            }
            var border = new Border
            {
                Child = strip,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16, 0, 16, 2),
                Padding = new Thickness(8, 3, 8, 3),
            };
            border.Bind(Border.BackgroundProperty, border.GetResourceObservable("PanelBgBrush"));
            border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("ChromeBorderBrush"));
            return border;
        }

        // --- Legend (colour words match the gauge markers) ---
        var legend = new TextBlock
        {
            FontSize = VarioMinimumFontSize,
            Opacity = 1.0,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0),
            TextWrapping = TextWrapping.NoWrap,
        };
        Run Swatch(string text, string brushKey)
        {
            var run = new Run(text) { FontWeight = FontWeight.Bold };
            run.Bind(TextElement.ForegroundProperty, run.GetResourceObservable(brushKey));
            return run;
        }
        var currentSwatch = Swatch("Red dashed", "VarioBadBrush");
        legend.Inlines = new InlineCollection
        {
            Swatch("Amber band", "VarioAcceptBandEdgeBrush"),
            new Run(" = acceptable band   "),
            Swatch("Blue solid", "VarioMinMaxBrush"),
            new Run(" = measured min/max   "),
            Swatch("Red solid", "VarioAverageBrush"),
            new Run(" = average   "),
            currentSwatch,
            new Run(" = current"),
        };

        // Scale the one-line legend down only when the window is too narrow to
        // show every word, so all glyphs stay visible at any width while keeping
        // the minimum font size whenever there is room.
        var legendBox = new Viewbox
        {
            Child = legend,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(16, 0, 16, 6),
        };

        Grid rateHeader = GaugeHeader("RATE (s/d)", "Acceptable band -10 to +10 s/d", out Border rateBandBadge);
        Grid amplitudeHeader = GaugeHeader("AMPLITUDE (°)", "Acceptable band 270 to 300°", out Border amplitudeBandBadge);
        Border rateReadout = BuildReadoutStrip(rateCells);
        Border amplitudeReadout = BuildReadoutStrip(amplitudeCells);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,*,Auto"),
        };
        Control[] rows =
        {
            summaryCard,
            rateHeader, rateReadout, ratePlot,
            amplitudeHeader, amplitudeReadout, amplitudePlot,
            legendBox,
        };
        for (int i = 0; i < rows.Length; i++)
        {
            Grid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 3);
            grid.Children.Add(overlay);
        }

        var summary = new VarioSummaryControls(
            rateStatus, ampStatus, elapsedValue, overallText);
        var renderer = new VarioRenderer(
            ratePlot, amplitudePlot, summary, new VarioBandBadgeControls(rateBandBadge, amplitudeBandBadge),
            new VarioReadoutControls(rateCells, amplitudeCells), context.TextFontFamily);
        var consumer = new VarioFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    /// <summary>
    /// The Vario "Criteria" flyout: the verdict thresholds, built from the same
    /// constants the evaluator uses so the popup cannot drift from the live rules.
    /// </summary>
    private static Control BuildVarioCriteria()
    {
        double band = VarioGaugePolicy.RateAcceptMaxSPerDay;
        double ampMin = VarioGaugePolicy.AmplitudeAcceptMinDeg;
        double ampMax = VarioGaugePolicy.AmplitudeAcceptMaxDeg;
        double service = VarioVerdict.AmplitudeServiceDeg;
        double sigma = VarioVerdict.RateUnstableSigma;

        TextBlock Title(string t) => new() { Text = t, FontWeight = FontWeight.Bold, FontSize = VarioMinimumFontSize, Margin = new Thickness(0, 6, 0, 2) };
        TextBlock Rule(string t, string brushKey)
        {
            var rule = new TextBlock
            {
                Text = t,
                FontSize = VarioMinimumFontSize,
                Margin = new Thickness(0, 1, 0, 1),
                MaxWidth = 320,
                TextWrapping = TextWrapping.Wrap,
            };
            rule.Bind(TextBlock.ForegroundProperty, rule.GetResourceObservable(brushKey));
            return rule;
        }

        var panel = new StackPanel { Margin = new Thickness(12), Width = 360, MaxWidth = 360 };
        panel.Children.Add(new TextBlock { Text = "Assessment criteria", FontWeight = FontWeight.Bold, FontSize = VarioMinimumFontSize });
        panel.Children.Add(new TextBlock
        {
            Text = $"Shown after {VarioVerdict.MinSamples} beats, classified from the average, for the current watch position.",
            FontSize = VarioMinimumFontSize,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            Margin = new Thickness(0, 2, 0, 0),
        });
        panel.Children.Add(Title("Rate (s/d)"));
        panel.Children.Add(Rule($"Stable · in range: average within ±{band:0} s/d and σ ≤ {sigma:0}", "VarioGoodBrush"));
        panel.Children.Add(Rule($"In range · unstable: average within ±{band:0} s/d but σ > {sigma:0}", "VarioWarnBrush"));
        panel.Children.Add(Rule($"Fast / Slow · out of range: average beyond ±{band:0} s/d", "VarioBadBrush"));
        panel.Children.Add(Title("Amplitude (°)"));
        panel.Children.Add(Rule($"Healthy: average {ampMin:0}–{ampMax:0}°", "VarioGoodBrush"));
        panel.Children.Add(Rule($"Slightly low / High: average {service:0}–{ampMin:0}° or above {ampMax:0}°", "VarioWarnBrush"));
        panel.Children.Add(Rule($"Low · service: average below {service:0}°", "VarioBadBrush"));
        return panel;
    }

    private static InfoTabRegistration CreateBeatErrorDiagRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var tracePlot = new AvaPlot();

        Border alertBanner = CreateAlertBanner(out TextBlock alertText);


        // Numeric panel: label/value cells for the plan readings (rate,
        // amplitude, beat error, BPH) on the top row and the derived
        // DiffTicTac / DiffPeriod / AvgPeriod measures on the bottom row.
        var valueTexts = new TextBlock[BeatErrorReadout.Labels.Length];
        var readoutGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(8, 4, 8, 2),
        };
        for (int i = 0; i < BeatErrorReadout.Labels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = BeatErrorReadout.Labels[i],
                FontSize = 11,
                Opacity = 0.65,
            };
            var value = new TextBlock
            {
                Text = VarioReadout.Missing,
                FontSize = 15,
            };
            valueTexts[i] = value;

            var cell = new StackPanel { Margin = new Thickness(0, 2, 12, 2) };
            cell.Children.Add(label);
            cell.Children.Add(value);
            Grid.SetRow(cell, i / 4);
            Grid.SetColumn(cell, i % 4);
            readoutGrid.Children.Add(cell);
        }

        var explanationText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.65,
            Margin = new Thickness(8, 0, 8, 3),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Tic/toc traces: horizontal = on time; a positive reading slopes the trace upward. " +
                   "Separation between the two traces = beat error; a slope past 45° flags a major fault.",
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
        };
        Grid.SetRow(alertBanner, 0);
        Grid.SetRow(readoutGrid, 1);
        Grid.SetRow(tracePlot, 2);
        Grid.SetRow(explanationText, 3);
        grid.Children.Add(alertBanner);
        grid.Children.Add(readoutGrid);
        grid.Children.Add(tracePlot);
        grid.Children.Add(explanationText);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 2);
            grid.Children.Add(overlay);
        }

        var renderer = new BeatErrorDiagRenderer(tracePlot, alertBanner, alertText, valueTexts);

        grid.Children.Add(CreatePinnedResetViewButton("Reset the trace view to its configured limits", row: 2, renderer.ResetView));

        var consumer = new BeatErrorDiagFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateMultiFilterScopeRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // Four vertically stacked plots (F0..F3 of the same signal), each under
        // its one-line description, so the filter views compare at a glance. The
        // raw waveform shows before beat sync, so no waiting overlay is added.
        _ = context;
        IReadOnlyList<MultiFilterScopeLane> lanes = MultiFilterScopeLanes.All;
        var plots = new AvaPlot[lanes.Count];
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions(
                string.Join(",", Enumerable.Repeat("Auto,*", lanes.Count))),
        };

        for (int i = 0; i < lanes.Count; i++)
        {
            var description = new TextBlock
            {
                Text = lanes[i].Label + " — " + lanes[i].Description,
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(8, 3, 8, 0),
            };
            plots[i] = new AvaPlot();
            Grid.SetRow(description, 2 * i);
            Grid.SetRow(plots[i], 2 * i + 1);
            grid.Children.Add(description);
            grid.Children.Add(plots[i]);
        }

        var renderer = new MultiFilterScopeRenderer(plots);
        grid.Children.Add(CreatePinnedResetViewButton(
            "Re-enable live windowing on all four lanes", row: 1, renderer.ResetView));
        var consumer = new MultiFilterScopeFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateLongTermPerfRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // Three stacked panes over compact 24-hour summary/navigation chrome.
        // The top strip is touch-friendly for Raspberry Pi use, while the old
        // explanatory legend is folded into the graph styling and quiet footer so
        // the plots keep as much vertical room as possible.
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        ratePlot.Margin = new Thickness(0, 0, 0, -8);
        amplitudePlot.Margin = new Thickness(0, -8, 0, -8);
        beatErrorPlot.Margin = new Thickness(0, -8, 0, 0);

        TextBlock SummaryValue(string text, double fontSize = 14) => new()
        {
            Text = text,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var verdictText = SummaryValue("COLLECTING", 14);
        var rateText = SummaryValue("RATE —");
        var amplitudeText = SummaryValue("AMPLITUDE —");
        var beatErrorText = SummaryValue("BEAT ERROR —");

        var summaryRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };
        summaryRow.Children.Add(verdictText);
        summaryRow.Children.Add(rateText);
        summaryRow.Children.Add(amplitudeText);
        summaryRow.Children.Add(beatErrorText);

        var summaryControls = new LongTermSummaryControls(
            verdictText,
            rateText,
            amplitudeText,
            beatErrorText);

        var footerText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 0, 8, 2),
            Opacity = 0.82,
        };

        var renderer = new LongTermPerfRenderer(
            ratePlot,
            amplitudePlot,
            beatErrorPlot,
            footerText,
            summaryControls);

        const string windowActiveClass = "active";
        var windowButtons = new List<(Button Button, double Seconds)>();

        Button NavButton(string content, string tooltip, Action onClick)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 36,
                MinHeight = 30,
                Padding = new Thickness(7, 2, 7, 2),
                FontSize = 12,
            };
            button.Classes.Add("PositionButton");
            ToolTip.SetTip(button, tooltip);
            button.Click += (_, _) => onClick();
            return button;
        }

        Button WindowButton(string content, double seconds, string tooltip)
        {
            Button button = NavButton(content, tooltip, () => renderer.ShowTimeWindow(seconds));
            windowButtons.Add((button, seconds));
            return button;
        }

        void UpdateWindowButtons(double seconds)
        {
            foreach ((Button button, double target) in windowButtons)
            {
                bool active = Math.Abs(seconds - target) <= Math.Max(1.0, target * 0.01);
                if (active && !button.Classes.Contains(windowActiveClass))
                {
                    button.Classes.Add(windowActiveClass);
                }
                else if (!active)
                {
                    button.Classes.Remove(windowActiveClass);
                }
            }
        }

        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        navigation.Children.Add(WindowButton("1h", 60 * 60, "Show the latest 1 hour"));
        navigation.Children.Add(WindowButton("3h", 3 * 60 * 60, "Show the latest 3 hours"));
        navigation.Children.Add(WindowButton("6h", 6 * 60 * 60, "Show the latest 6 hours"));
        navigation.Children.Add(NavButton("‹", "Pan earlier", renderer.PanLeft));
        navigation.Children.Add(NavButton("›", "Pan later", renderer.PanRight));
        renderer.SetVisibleWindowCallback(UpdateWindowButtons);
        UpdateWindowButtons(24 * 60 * 60);

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
            ClipToBounds = true,
        };
        Grid.SetColumn(summaryRow, 0);
        Grid.SetColumn(navigation, 1);
        headerGrid.Children.Add(summaryRow);
        headerGrid.Children.Add(navigation);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,*,*,Auto"),
        };
        Control[] rows = { headerGrid, ratePlot, amplitudePlot, beatErrorPlot, footerText };
        for (int i = 0; i < rows.Length; i++)
        {
            Grid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        if (context.ViewModel is { } vm)
        {
            renderer.SetSliderAlignmentCallback(vm.UpdateReviewSliderAlignment);
            renderer.SetReviewMetricsCallback(vm.UpdateReviewMetricsText);
        }

        var consumer = new LongTermPerfFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateTestPositionsRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        IReadOnlyList<WatchPosition> positions = WatchPositions.All;
        var buttons = new Button[positions.Count];
        Grid buttonGrid = context.PositionButtonGrid;
        buttonGrid.Children.Clear();
        buttonGrid.ColumnDefinitions = new ColumnDefinitions("*");
        buttonGrid.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("*", positions.Count)));
        buttonGrid.MinWidth = 76;
        buttonGrid.MaxWidth = 92;
        buttonGrid.VerticalAlignment = VerticalAlignment.Stretch;

        for (int i = 0; i < positions.Count; i++)
        {
            WatchPosition position = positions[i];
            var shortText = new TextBlock
            {
                Text = position.ShortName(),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };

            var button = new Button
            {
                Content = shortText,
                Classes = { "PositionButton" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(2),
                Padding = new Thickness(4, 3),
            };
            ToolTip.SetTip(button, $"Tag new measurements as {position.ShortName()} - {position.LongName()}");
            Grid.SetRow(button, i);
            buttons[i] = button;
            buttonGrid.Children.Add(button);
        }

        var initialPosition = (WatchPosition)(context.ViewModel?.SelectedPositionIndex ?? 0);
        var diagram = new WatchPositionDiagram
        {
            Position = initialPosition,
            ShowLabels = false,
            Width = 236,
            Height = 126,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(diagram, "Active watch position orientation");
        var positionRenderer = new TestPositionsRenderer(buttons, diagram, initialPosition);

        for (int i = 0; i < buttons.Length; i++)
        {
            var position = (WatchPosition)i;
            buttons[i].Click += (_, _) =>
            {
                if (context.ViewModel is { } viewModel)
                {
                    viewModel.SelectedPositionIndex = (int)position;
                }

                positionRenderer.RequestPosition(position);
            };
        }

        Border alertBanner = CreateAlertBanner(out TextBlock alertText);

        var tableGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"),
            Margin = new Thickness(10, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        Border activePanel = CreateActivePositionPanel(
            diagram,
            out TextBlock activePositionText,
            out TextBlock activeOrientationText);
        Grid positionMap = CreatePositionMap(out IReadOnlyList<PositionMapTileControls> positionMapTiles);
        Border resultPanel = CreatePositionResultPanel(
            out Border consistencyBadge,
            out TextBlock consistencyVerdictText,
            out TextBlock consistencyDetailText,
            out TextBlock consistencyGuideText,
            out TextBlock averageRateText,
            out TextBlock averageAmplitudeText,
            out TextBlock spreadRateText,
            out TextBlock spreadAmplitudeText,
            out TextBlock verticalRateText,
            out TextBlock horizontalRateText,
            out TextBlock verticalHorizontalDeltaText);
        var dashboardControls = new PositionSequenceDashboardControls(
            activePositionText,
            activeOrientationText,
            positionMapTiles,
            consistencyBadge,
            consistencyVerdictText,
            consistencyDetailText,
            consistencyGuideText,
            averageRateText,
            averageAmplitudeText,
            spreadRateText,
            spreadAmplitudeText,
            verticalRateText,
            horizontalRateText,
            verticalHorizontalDeltaText);

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("276,*"),
            RowDefinitions = new RowDefinitions("*"),
        };
        Grid.SetColumn(activePanel, 0);
        Grid.SetColumn(tableGrid, 1);
        topGrid.Children.Add(activePanel);
        topGrid.Children.Add(tableGrid);

        var sequenceGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            Margin = new Thickness(4, 4, 8, 4),
        };
        Grid.SetRow(alertBanner, 0);
        Grid.SetRow(topGrid, 1);
        Grid.SetRow(positionMap, 2);
        Grid.SetRow(resultPanel, 3);
        sequenceGrid.Children.Add(alertBanner);
        sequenceGrid.Children.Add(topGrid);
        sequenceGrid.Children.Add(positionMap);
        sequenceGrid.Children.Add(resultPanel);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            Grid.SetRowSpan(overlay, 4);
            sequenceGrid.Children.Add(overlay);
        }

        var sequenceRenderer = new MultiPositionSeqRenderer(
            tableGrid,
            alertBanner,
            alertText,
            dashboardControls,
            initialPosition);
        var consumer = new TestPositionsFrameConsumer(positionRenderer, sequenceRenderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, sequenceGrid), consumer);
    }

    private static Border CreateActivePositionPanel(
        WatchPositionDiagram diagram,
        out TextBlock activePositionText,
        out TextBlock activeOrientationText)
    {
        activePositionText = new TextBlock
        {
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        activeOrientationText = new TextBlock
        {
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        var stack = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                CreatePositionSectionHeader("ACTIVE"),
                diagram,
                activePositionText,
                activeOrientationText,
            },
        };

        return new Border
        {
            Classes = { "PositionPanel" },
            Padding = new Thickness(10, 8),
            Margin = new Thickness(4, 0, 10, 0),
            Child = stack,
        };
    }

    private static Grid CreatePositionMap(out IReadOnlyList<PositionMapTileControls> positionMapTiles)
    {
        var tiles = new List<PositionMapTileControls>(WatchPositions.Count);
        var tileGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"),
            RowDefinitions = new RowDefinitions("50,50"),
            VerticalAlignment = VerticalAlignment.Top,
        };

        IReadOnlyList<WatchPosition> positions = WatchPositions.All;
        for (int i = 0; i < positions.Count; i++)
        {
            WatchPosition position = positions[i];
            var tile = new Border
            {
                Classes = { "PositionMapTile" },
                Margin = new Thickness(4, 2),
                Padding = new Thickness(8, 3),
                Height = 46,
                ClipToBounds = true,
                Child = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = position.ShortName(),
                            FontSize = 16,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = position.LongName(),
                            FontSize = PositionMinimumFontSize,
                            Opacity = 0.82,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        },
                    },
                },
            };
            ToolTip.SetTip(tile, position.LongName());
            Grid.SetColumn(tile, i % 5);
            Grid.SetRow(tile, i / 5);
            tileGrid.Children.Add(tile);
            tiles.Add(new PositionMapTileControls(position, tile));
        }

        var map = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(4, 4, 0, 2),
        };
        TextBlock header = CreatePositionSectionHeader("POSITION MAP");
        Grid.SetRow(header, 0);
        Grid.SetRow(tileGrid, 1);
        map.Children.Add(header);
        map.Children.Add(tileGrid);

        positionMapTiles = tiles;
        return map;
    }

    private static Border CreatePositionResultPanel(
        out Border consistencyBadge,
        out TextBlock consistencyVerdictText,
        out TextBlock consistencyDetailText,
        out TextBlock consistencyGuideText,
        out TextBlock averageRateText,
        out TextBlock averageAmplitudeText,
        out TextBlock spreadRateText,
        out TextBlock spreadAmplitudeText,
        out TextBlock verticalRateText,
        out TextBlock horizontalRateText,
        out TextBlock verticalHorizontalDeltaText)
    {
        averageRateText = CreatePositionSummaryValue();
        averageAmplitudeText = CreatePositionSummaryValue();
        spreadRateText = CreatePositionSummaryValue();
        spreadAmplitudeText = CreatePositionSummaryValue();
        verticalRateText = CreatePositionSummaryValue();
        horizontalRateText = CreatePositionSummaryValue();
        verticalHorizontalDeltaText = CreatePositionSummaryValue();
        consistencyVerdictText = new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        consistencyDetailText = new TextBlock
        {
            FontSize = PositionMinimumFontSize,
            Opacity = 0.82,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        consistencyBadge = new Border
        {
            Classes = { "PositionResultBadge" },
            MinWidth = 136,
            Padding = new Thickness(16, 5),
            Child = consistencyVerdictText,
        };
        consistencyGuideText = new TextBlock
        {
            FontSize = PositionMinimumFontSize,
            FontWeight = FontWeight.Bold,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var criteriaButton = new Button
        {
            Content = "View criteria ▾",
            FontSize = PositionMinimumFontSize,
            MinWidth = 148,
            MinHeight = 32,
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                Content = BuildPositionCriteria(),
            },
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,*,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 6),
        };
        var title = new TextBlock
        {
            Text = "POSITION CONSISTENCY",
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        consistencyDetailText.Margin = new Thickness(14, 0, 16, 0);
        Grid.SetColumn(title, 0);
        Grid.SetColumn(consistencyDetailText, 1);
        Grid.SetColumn(criteriaButton, 2);
        Grid.SetColumn(consistencyBadge, 3);
        header.Children.Add(title);
        header.Children.Add(consistencyDetailText);
        header.Children.Add(criteriaButton);
        header.Children.Add(consistencyBadge);

        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        Border spreadGroup = CreatePositionResultGroup(
            "D SPREAD",
            "Worst - best across positions.",
            ("RATE", spreadRateText),
            ("AMPLITUDE", spreadAmplitudeText));
        Border vhGroup = CreatePositionResultGroup(
            "V/H BALANCE",
            "Vertical mean - horizontal mean.",
            ("VERT", verticalRateText),
            ("HORIZ", horizontalRateText),
            ("DVH", verticalHorizontalDeltaText));
        Border averageGroup = CreatePositionResultGroup(
            "X AVERAGE",
            "Mean of measured positions.",
            ("RATE", averageRateText),
            ("AMPLITUDE", averageAmplitudeText));
        spreadGroup.Classes.Add("primary");
        vhGroup.Classes.Add("primary");

        Grid.SetColumn(spreadGroup, 0);
        Grid.SetColumn(vhGroup, 1);
        Grid.SetColumn(averageGroup, 2);
        metrics.Children.Add(spreadGroup);
        metrics.Children.Add(vhGroup);
        metrics.Children.Add(averageGroup);

        var explanationText = new TextBlock
        {
            FontSize = PositionMinimumFontSize,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Text = "Verdict starts at 3 positions with 30+ beats. Later qualified positions update the result.",
        };

        var panelGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(consistencyGuideText, 1);
        Grid.SetRow(metrics, 2);
        Grid.SetRow(explanationText, 3);
        panelGrid.Children.Add(header);
        panelGrid.Children.Add(consistencyGuideText);
        panelGrid.Children.Add(metrics);
        panelGrid.Children.Add(explanationText);

        return new Border
        {
            Classes = { "PositionResultPanel" },
            Padding = new Thickness(16, 8),
            Margin = new Thickness(4, 2, 8, 2),
            Child = panelGrid,
        };
    }

    private static Border CreatePositionResultGroup(
        string title,
        string description,
        params (string Label, TextBlock Value)[] metrics)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            Opacity = 0.9,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = PositionMinimumFontSize,
            Opacity = 0.76,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        foreach ((string label, TextBlock value) in metrics)
        {
            stack.Children.Add(CreatePositionMetricRow(label, value));
        }

        return new Border
        {
            Classes = { "PositionResultGroup" },
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 0, 12, 0),
            Child = stack,
        };
    }

    private static Grid CreatePositionMetricRow(string label, TextBlock value)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("112,*"),
        };
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = PositionMinimumFontSize,
            Opacity = 0.78,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(labelText);
        row.Children.Add(value);
        return row;
    }

    private static Control BuildPositionCriteria()
    {
        const double threshold = SequenceSummary.UnbalanceVerticalRateSpreadSPerDay;

        TextBlock Title(string text) => new()
        {
            Text = text,
            FontWeight = FontWeight.Bold,
            FontSize = PositionMinimumFontSize,
            Margin = new Thickness(0, 6, 0, 2),
        };

        TextBlock Rule(string text, string brushKey)
        {
            var rule = new TextBlock
            {
                Text = text,
                FontSize = PositionMinimumFontSize,
                Margin = new Thickness(0, 1, 0, 1),
                MaxWidth = 340,
                TextWrapping = TextWrapping.Wrap,
            };
            rule.Bind(TextBlock.ForegroundProperty, rule.GetResourceObservable(brushKey));
            return rule;
        }

        var panel = new StackPanel { Margin = new Thickness(12), Width = 380, MaxWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = "Position criteria",
            FontWeight = FontWeight.Bold,
            FontSize = PositionMinimumFontSize,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "A qualified position has a rate result with 30+ beats. The final verdict starts after 3 qualified positions and updates as later positions qualify.",
            FontSize = PositionMinimumFontSize,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
            Margin = new Thickness(0, 2, 0, 0),
        });
        panel.Children.Add(Title("D Spread"));
        panel.Children.Add(Rule($"Basis: max - min rate across all qualified positions. CHECK above {threshold:0} s/d.", "VarioWarnBrush"));
        panel.Children.Add(Rule("Meaning: high positional variation. It does not identify the mechanical cause by itself.", "TextPrimaryBrush"));
        panel.Children.Add(Title("Balance-wheel"));
        panel.Children.Add(Rule($"Basis: vertical spread across 2+ full vertical positions. CHECK above {threshold:0} s/d.", "VarioWarnBrush"));
        panel.Children.Add(Rule("Meaning: possible balance-wheel centering or balancing issue.", "TextPrimaryBrush"));
        panel.Children.Add(Title("V/H Balance"));
        panel.Children.Add(Rule("Basis: vertical mean - horizontal mean; needs at least 1 vertical and 1 horizontal position.", "VarioWarnBrush"));
        panel.Children.Add(Rule("Meaning: vertical-vs-horizontal bias. Treat it separately from balance-wheel unbalance.", "TextPrimaryBrush"));
        return panel;
    }

    private static TextBlock CreatePositionSectionHeader(string text) => new()
    {
        Text = text,
        FontSize = PositionMinimumFontSize,
        Opacity = 0.75,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    private static TextBlock CreatePositionSummaryValue() => new()
    {
        FontSize = 22,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextAlignment = TextAlignment.Right,
    };

    private static InfoTabRegistration CreateBeatNoiseScopeRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // Scope 1: toolbar (mode / range / absolute-value toggle / Σ / lift angle), the enlarged
        // selected-or-latest beat, and the aligned strip lane of the 8 most
        // recent beats. Scope 2: the two averaged lanes above their readout.
        var mainPlot = new AvaPlot();
        var stripPlot = new AvaPlot
        {
            Height = 208,
        };
        var averagePlot = new AvaPlot();

        var liftText = new TextBlock
        {
            Text = "LIFT —",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        var averageText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 2),
        };

        var renderer = new BeatNoiseScopeRenderer(mainPlot, stripPlot, averagePlot, liftText, averageText);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 4),
        };

        var envelopeModeButton = new Button
        {
            Content = "Beat Scope",
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
        };
        ToolTip.SetTip(envelopeModeButton, "Show beat-noise waveform + Strip mode (absolute-value display controlled separately)");
        toolbar.Children.Add(envelopeModeButton);

        var averageModeButton = new Button
        {
            Content = "Avg Envelope",
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(averageModeButton, "Show Average Envelope + Strip mode");
        toolbar.Children.Add(averageModeButton);

        // 20 / 200 / 400 ms range selector; the active range renders disabled
        // (the Scope Sweep 1x/2x/3x button pattern).
        int[] ranges = { 20, 200, 400 };
        var rangeButtons = new Button[ranges.Length];

        void UpdateRangeButtonStates()
        {
            for (int i = 0; i < rangeButtons.Length; i++)
            {
                rangeButtons[i].IsEnabled = ranges[i] != renderer.RangeMs;
            }
        }

        for (int i = 0; i < ranges.Length; i++)
        {
            int rangeMs = ranges[i];
            var button = new Button
            {
                Content = rangeMs + " ms",
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11,
            };
            ToolTip.SetTip(button, $"Show the first {rangeMs} ms of the beat window");
            button.Click += (_, _) =>
            {
                renderer.SetRangeMs(rangeMs);
                UpdateRangeButtonStates();
            };
            rangeButtons[i] = button;
            toolbar.Children.Add(button);
        }

        UpdateRangeButtonStates();

        var absoluteToggle = new ToggleButton
        {
            Content = "ABS",
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ToolTip.SetTip(absoluteToggle, "On: show rectified absolute-value envelope. Off: show real bipolar waveform (min/max).");
        absoluteToggle.IsCheckedChanged += (_, _) => renderer.SetAbsoluteValue(absoluteToggle.IsChecked == true);
        toolbar.Children.Add(absoluteToggle);

        // Σ writes the shared SigmaAveraging view-model property; MainWindow
        // forwards the change to the running analysis worker (the
        // SetSweepMultiple flow). Display state comes back via the snapshot.
        var sigmaToggle = new ToggleButton
        {
            Content = "Σ",
            Padding = new Thickness(10, 2, 10, 2),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            IsChecked = context.ViewModel?.SigmaAveraging == true,
        };
        ToolTip.SetTip(sigmaToggle, "Average 50 + 50 beat noises into the two Scope 2 traces");
        sigmaToggle.IsCheckedChanged += (_, _) =>
        {
            if (context.ViewModel is { } viewModel)
            {
                viewModel.SigmaAveraging = sigmaToggle.IsChecked == true;
            }
        };
        toolbar.Children.Add(sigmaToggle);

        toolbar.Children.Add(liftText);

        // Strip-lane hit test maps the pointer through the aligned data area,
        // excluding the reserved left axis width used to match the top plot.
        stripPlot.PointerPressed += (_, e) =>
        {
            if (stripPlot.Bounds.Width > 0)
            {
                renderer.SelectStripAtPixel(e.GetPosition(stripPlot).X, stripPlot.Bounds.Width);
            }
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
        };
        Control[] rows = { toolbar, mainPlot, stripPlot, averageText };
        for (int i = 0; i < rows.Length; i++)
        {
            Grid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }

        // Put averagePlot in the same row as mainPlot
        Grid.SetRow(averagePlot, 1);
        grid.Children.Add(averagePlot);

        void ApplyViewMode(BeatNoiseScopeViewMode mode)
        {
            bool isAverageMode = mode == BeatNoiseScopeViewMode.AverageAndStrip;
            renderer.SetViewMode(mode);
            envelopeModeButton.IsEnabled = isAverageMode;
            averageModeButton.IsEnabled = !isAverageMode;

            foreach (var button in rangeButtons)
            {
                button.IsVisible = !isAverageMode;
            }

            absoluteToggle.IsVisible = !isAverageMode;
            mainPlot.IsVisible = !isAverageMode;
            averagePlot.IsVisible = isAverageMode;
            averageText.IsVisible = isAverageMode;
            sigmaToggle.IsVisible = isAverageMode;
        }

        envelopeModeButton.Click += (_, _) => ApplyViewMode(BeatNoiseScopeViewMode.EnvelopeAndStrip);
        averageModeButton.Click += (_, _) => ApplyViewMode(BeatNoiseScopeViewMode.AverageAndStrip);
        ApplyViewMode(BeatNoiseScopeViewMode.EnvelopeAndStrip);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var consumer = new BeatNoiseScopeFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateEscapementAnalyzerRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // One large plot of the latest beat's envelope with the A / C marker
        // lines and millisecond labels, above the numeric repeatability panel:
        // label/value cells (the BeatErrorDiag pattern) for the current A→C
        // readings per reference, the onset-vs-peak delta, the windowed
        // mean±sigma of both references and the more-repeatable verdict.
        var markerPlot = new AvaPlot();

        var valueTexts = new TextBlock[EscapementReadout.Labels.Length];
        var readoutGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(8, 4, 8, 2),
        };
        for (int i = 0; i < EscapementReadout.Labels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = EscapementReadout.Labels[i],
                FontSize = 11,
                Opacity = 0.65,
            };
            var value = new TextBlock
            {
                Text = VarioReadout.Missing,
                FontSize = 15,
            };
            valueTexts[i] = value;

            var cell = new StackPanel { Margin = new Thickness(0, 2, 12, 2) };
            cell.Children.Add(label);
            cell.Children.Add(value);
            Grid.SetRow(cell, i / 3);
            Grid.SetColumn(cell, i % 3);
            readoutGrid.Children.Add(cell);
        }

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetRow(markerPlot, 0);
        Grid.SetRow(readoutGrid, 1);
        grid.Children.Add(markerPlot);
        grid.Children.Add(readoutGrid);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 0);
            grid.Children.Add(overlay);
        }

        var renderer = new EscapementAnalyzerRenderer(markerPlot, valueTexts, context.TextFontFamily);
        var consumer = new EscapementAnalyzerFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateWaveformCompareRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // Header numeric line (rate / beat error / BPH) above one plot that
        // stacks the recent beats in A-aligned, peak-normalized lanes, and a
        // one-line legend for the guide markers below.
        var headerText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 4, 8, 2),
        };
        var lanePlot = new AvaPlot();
        var explanationText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.65,
            Margin = new Thickness(8, 0, 8, 3),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Each lane is one recent beat (oldest at the bottom), normalized to its own " +
                   "peak and aligned at the A event (x = 0). Green guide = A · red guide = mean " +
                   "C peak of the shown beats; beats whose C strays from the guide reveal " +
                   "spacing inconsistency.",
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid.SetRow(headerText, 0);
        Grid.SetRow(lanePlot, 1);
        Grid.SetRow(explanationText, 2);
        grid.Children.Add(headerText);
        grid.Children.Add(lanePlot);
        grid.Children.Add(explanationText);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var renderer = new WaveformCompareRenderer(lanePlot, headerText, context.TextFontFamily);
        var consumer = new WaveformCompareFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    // Centered "waiting for beat sync" label, shown while a run has not yet locked the
    // tick/tock beat. Foreground/font come from the global TextBlock style (themed).
    private static TextBlock? CreateWaitingOverlay(MainWindowViewModel? viewModel)
    {
        if (viewModel == null)
        {
            return null;
        }

        var overlay = new TextBlock
        {
            Text = "Waiting for tick-tock sync…",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        overlay.Bind(
            Visual.IsVisibleProperty,
            new Binding(nameof(MainWindowViewModel.IsAwaitingBeatSync)) { Source = viewModel });
        return overlay;
    }

    private static TabItem CreateTabItem(InfoTabDefinition definition, Control content)
    {
        return new TabItem
        {
            Header = definition.Title,
            Tag = definition.Id,
            Content = content,
        };
    }
}
