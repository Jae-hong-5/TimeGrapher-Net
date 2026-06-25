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
    private const double SweepReferenceRowHeight = 22.0;
    private const string ResetAllGraphViewsTooltip = "Reset all graph views";

    private delegate InfoTabRegistration InfoTabFactory(
        InfoTabDefinition definition,
        InfoTabFactoryContext context);

    private sealed class InfoTabFactoryContext
    {
        public required string TextFontFamily { get; init; }
        public required Grid PositionButtonGrid { get; init; }
        public MainWindowViewModel? ViewModel { get; init; }
        public Image? SoundImageControl { get; set; }
        public GraphViewResetCoordinator ResetViews { get; } = new();
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
            [InfoTabKind.WatchPositions] = CreateWatchPositionsRegistration,
            [InfoTabKind.WatchHealthRadar] = CreateWatchHealthRadarRegistration,
            [InfoTabKind.BeatNoiseScope] = CreateBeatNoiseScopeRegistration,
            [InfoTabKind.EscapementAnalyzer] = CreateEscapementAnalyzerRegistration,
            [InfoTabKind.WaveformCompare] = CreateWaveformCompareRegistration,
            [InfoTabKind.Spectrogram] = CreateSpectrogramRegistration,
        };

    private readonly IReadOnlyList<InfoTabRegistration> _registrations;
    private readonly IAnalysisFrameConsumer[] _consumers;
    private readonly GraphViewResetCoordinator _resetViews;

    private InfoTabRegistry(
        IReadOnlyList<InfoTabRegistration> registrations,
        Image? soundImageControl,
        GraphViewResetCoordinator resetViews)
    {
        _registrations = registrations;
        _consumers = registrations.Select(registration => registration.Consumer).ToArray();
        SoundImageControl = soundImageControl;
        _resetViews = resetViews;
    }

    public IReadOnlyList<InfoTabRegistration> Registrations => _registrations;
    public IReadOnlyList<IAnalysisFrameConsumer> Consumers => _consumers;
    public Image? SoundImageControl { get; }
    public GraphViewResetCoordinator ResetViews => _resetViews;

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

        return new InfoTabRegistry(registrations, context.SoundImageControl, context.ResetViews);
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

    private static Border CreateLongTermReviewBar()
    {
        Button ReviewButton(string name, string content, string commandPath, string tooltip, Thickness margin = default)
        {
            var button = new Button
            {
                Name = name,
                Content = content,
                FontSize = 11,
                Padding = new Thickness(6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin,
            };
            button.Bind(Button.CommandProperty, new Binding(commandPath));
            ToolTip.SetTip(button, tooltip);
            return button;
        }

        var stepControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        stepControls.Children.Add(ReviewButton(
            "ReviewStepBackButton",
            "-1 s",
            nameof(MainWindowViewModel.ReviewStepBackCommand),
            "Move the review cursor 1 second back",
            new Thickness(0, 0, 4, 0)));
        stepControls.Children.Add(ReviewButton(
            "ReviewStepForwardButton",
            "+1 s",
            nameof(MainWindowViewModel.ReviewStepForwardCommand),
            "Move the review cursor 1 second forward"));

        var readoutText = new TextBlock
        {
            Name = "ReviewReadoutLabel",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 130,
        };
        readoutText.Bind(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.ReviewReadoutText)));
        ToolTip.SetTip(readoutText, "Review cursor position / latest captured time");

        var metricsText = new TextBlock
        {
            Name = "ReviewMetricsLabel",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        metricsText.Bind(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.ReviewMetricsText)));
        metricsText.Bind(TextBlock.ForegroundProperty, metricsText.GetResourceObservable("TextPrimaryBrush"));
        ToolTip.SetTip(metricsText, "Rate, amplitude, and beat error at the review cursor");

        var liveAndReadoutControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        liveAndReadoutControls.Children.Add(ReviewButton(
            "ReviewLiveButton",
            "LIVE",
            nameof(MainWindowViewModel.ReviewLiveCommand),
            "Clear the review cursor (back to the newest reading)",
            new Thickness(0, 0, 6, 0)));
        liveAndReadoutControls.Children.Add(readoutText);
        liveAndReadoutControls.Children.Add(metricsText);

        var slider = new Slider
        {
            Name = "ReviewSlider",
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Compact the slider strip. The Fluent horizontal slider wraps its track in
        // 15 px top/bottom content-margin rows over a 32 px min height, which dwarfs
        // the 18 px thumb and leaves wide whitespace above/below the track. Shadow
        // those resources on the slider itself so the strip is no taller than the
        // thumb; scoped here (not app-wide) to leave the gain slider's 32 px
        // control-row height — aligned with its sibling combo boxes — intact.
        slider.Resources.Add("SliderHorizontalHeight", 18.0);
        slider.Resources.Add("SliderPreContentMargin", new GridLength(0));
        slider.Resources.Add("SliderPostContentMargin", new GridLength(0));
        slider.Bind(Layoutable.MarginProperty, new MultiBinding
        {
            Converter = new ReviewSliderMarginConverter(),
            Bindings =
            {
                new Binding(nameof(MainWindowViewModel.ReviewSliderLeftMargin)),
                new Binding(nameof(MainWindowViewModel.ReviewSliderRightMargin)),
            },
        });
        slider.Bind(RangeBase.MinimumProperty, new Binding(nameof(MainWindowViewModel.ReviewMinimumS)));
        slider.Bind(RangeBase.MaximumProperty, new Binding(nameof(MainWindowViewModel.ReviewMaximumS)));
        slider.Bind(
            RangeBase.ValueProperty,
            new Binding(nameof(MainWindowViewModel.ReviewSliderValueS)) { Mode = BindingMode.TwoWay });
        ToolTip.SetTip(slider, "Scrub backward/forward through the captured readings");

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(8, 2, 10, 2),
        };
        Grid.SetRow(stepControls, 0);
        Grid.SetRow(liveAndReadoutControls, 0);
        Grid.SetRow(slider, 1);
        grid.Children.Add(stepControls);
        grid.Children.Add(liveAndReadoutControls);
        grid.Children.Add(slider);

        var reviewBar = new Border
        {
            Name = "ReviewBar",
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
        reviewBar.Bind(Border.BackgroundProperty, reviewBar.GetResourceObservable("SurfaceBgBrush"));
        reviewBar.Bind(Border.BorderBrushProperty, reviewBar.GetResourceObservable("ChromeBorderBrush"));
        reviewBar.Bind(
            InputElement.IsEnabledProperty,
            new Binding(nameof(MainWindowViewModel.IsReviewBarEnabled)));
        return reviewBar;
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
        Button resetViewButton = CreateOverlayButton(
            "Reset View", ResetAllGraphViewsTooltip, context.ResetViews.ResetAll);
        resetViewButton.MinHeight = TraceHeaderButtonMinHeight;
        resetViewButton.FontSize = TraceHeaderButtonFontSize;
        resetViewButton.Padding = TraceHeaderButtonPadding;
        resetViewButton.VerticalAlignment = VerticalAlignment.Center;
        resetViewButton.Classes.Add("PositionButton");
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        controls.Children.Add(resetViewButton);
        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };
        Grid.SetColumn(controls, 1);
        headerStrip.Children.Add(controls);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,*"),
        };
        Grid.SetRow(headerStrip, 0);
        Grid.SetRow(ratePlot, 1);
        Grid.SetRow(scopePlot, 2);
        grid.Children.Add(headerStrip);
        grid.Children.Add(ratePlot);
        grid.Children.Add(scopePlot);

        // "Waiting for beat sync" overlay sits over the Error Rate plot (the scope
        // below already shows the live waveform before sync).
        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var renderer = new RateScopeRenderer(scopePlot, ratePlot, context.TextFontFamily);
        context.ResetViews.Register(renderer.ResetRateView);
        context.ResetViews.Register(renderer.ResetScopeView);

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

        var renderer = new TraceDisplayRenderer(ratePlot, amplitudePlot, alertBanner, alertText);
        context.ResetViews.Register(renderer.ResetView);

        // Top strip (the Long-Term header pattern): the conditional alert banner
        // holds the left "*" column so it shows and hides in place, and the right
        // column carries the Smoothing toggle followed by Reset View. The
        // always-present buttons fix the strip's height, so the plots below no
        // longer shift up and down as the banner appears and clears.
        Button smoothingButton = CreateTraceSmoothingButton(renderer);
        // Reset View sized to match the Smoothing button (same height/font/padding
        // and the shared button style) so the pair reads as one control group.
        Button resetViewButton = CreateOverlayButton(
            "Reset View", ResetAllGraphViewsTooltip, context.ResetViews.ResetAll);
        resetViewButton.MinHeight = TraceHeaderButtonMinHeight;
        resetViewButton.FontSize = TraceHeaderButtonFontSize;
        resetViewButton.Padding = TraceHeaderButtonPadding;
        resetViewButton.VerticalAlignment = VerticalAlignment.Center;
        resetViewButton.Classes.Add("PositionButton");
        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerButtons.Children.Add(smoothingButton);
        headerButtons.Children.Add(resetViewButton);

        // Keep a gap to the left of the buttons so the conditional alert banner
        // never butts up against Smoothing when it appears.
        alertBanner.VerticalAlignment = VerticalAlignment.Center;
        alertBanner.Margin = new Thickness(0, 0, 8, 0);
        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };
        Grid.SetColumn(alertBanner, 0);
        Grid.SetColumn(headerButtons, 1);
        headerStrip.Children.Add(alertBanner);
        headerStrip.Children.Add(headerButtons);

        // The amplitude (bottom) pane shows the shared time axis while the rate
        // (top) pane hides its X axis (the Long-Term pattern), so the amplitude row
        // is enlarged to keep both DATA areas the same height. Tuned for the
        // 1280x750 design size.
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,1.11*"),
        };
        Grid.SetRow(headerStrip, 0);
        Grid.SetRow(ratePlot, 1);
        Grid.SetRow(amplitudePlot, 2);
        grid.Children.Add(headerStrip);
        grid.Children.Add(ratePlot);
        grid.Children.Add(amplitudePlot);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var consumer = new TraceDisplayFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    // Shared dimensions for the Trace header buttons so Smoothing and Reset View
    // stay the same size as one matched control group.
    private const double TraceHeaderButtonMinHeight = 30;
    private const double TraceHeaderButtonFontSize = 12;
    private static readonly Thickness TraceHeaderButtonPadding = new(10, 2, 10, 2);

    /// <summary>
    /// Smoothing toggle for the Trace tab, styled like the Long-Term header
    /// buttons: clicking flips spline (smooth-curve) rendering of both traces and
    /// reflects the state with the shared active-button accent.
    /// </summary>
    private static Button CreateTraceSmoothingButton(TraceDisplayRenderer renderer)
    {
        const string activeClass = "active";
        var button = new Button
        {
            Content = "Smoothing",
            MinHeight = TraceHeaderButtonMinHeight,
            Padding = TraceHeaderButtonPadding,
            FontSize = TraceHeaderButtonFontSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("PositionButton");
        button.Classes.Add(activeClass);
        ToolTip.SetTip(button, "Draw the rate and amplitude traces as smooth spline curves");

        bool smoothing = true;
        button.Click += (_, _) =>
        {
            smoothing = !smoothing;
            renderer.SetSmoothing(smoothing);
            if (smoothing)
            {
                button.Classes.Add(activeClass);
            }
            else
            {
                button.Classes.Remove(activeClass);
            }
        };
        return button;
    }

    private static InfoTabRegistration CreateScopeSweepRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var sweepPlot = new AvaPlot();

        var referenceText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 0, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ClipToBounds = true,
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions($"Auto,*,{SweepReferenceRowHeight}"),
        };
        Grid.SetRow(sweepPlot, 1);
        Grid.SetRow(referenceText, 2);
        grid.Children.Add(sweepPlot);
        grid.Children.Add(referenceText);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Button SweepHeaderButton(string content, string tooltip, Action onClick)
        {
            Button button = CreateOverlayButton(content, tooltip, onClick);
            button.MinHeight = TraceHeaderButtonMinHeight;
            button.FontSize = TraceHeaderButtonFontSize;
            button.Padding = TraceHeaderButtonPadding;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Classes.Add("PositionButton");
            return button;
        }

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

            for (int i = 0; i < multiples.Length; i++)
            {
                int multiple = multiples[i];
                Button button = SweepHeaderButton(
                    multiple + "x",
                    $"Sweep window = {multiple}x the tick-tick interval",
                    () =>
                {
                    viewModel.SweepMultiple = multiple;
                    UpdateButtonStates();
                });
                buttons[i] = button;
                buttonRow.Children.Add(button);
            }

            UpdateButtonStates();
        }

        var renderer = new ScopeSweepRenderer(sweepPlot, referenceText, context.TextFontFamily);
        context.ResetViews.Register(renderer.ResetView);
        buttonRow.Children.Add(SweepHeaderButton(
            "Reset View", ResetAllGraphViewsTooltip, context.ResetViews.ResetAll));

        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };
        Grid.SetColumn(buttonRow, 1);
        headerStrip.Children.Add(buttonRow);
        Grid.SetRow(headerStrip, 0);
        grid.Children.Add(headerStrip);

        var consumer = new ScopeSweepFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateVarioRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var font = new FontFamily(context.TextFontFamily);

        Grid GaugeHeader(string text, string bandText, out Border bandBadge, out TextBlock badgeLabel)
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
            // The renderer rewrites this label from the live band on CreateGraphs /
            // ApplyAcceptBands; the literal here is only a pre-init placeholder.
            badgeLabel = new TextBlock
            {
                Text = bandText,
                FontSize = VarioMinimumFontSize,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bandBadge = new Border
            {
                BorderThickness = new Thickness(1),
                IsVisible = false,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(10, 0, 0, 0),
                Child = badgeLabel,
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
        var amplitudeStatus = new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold };
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

        var criteriaFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = BuildVarioCriteria(),
        };
        // Rebuild the criteria content on every open so it always states the live
        // (possibly edited) bands, not values snapshotted once at tab construction.
        criteriaFlyout.Opening += (_, _) => criteriaFlyout.Content = BuildVarioCriteria();
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
            Flyout = criteriaFlyout,
        };

        var summaryColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,160") };
        Control[] summaryCells =
        {
            SummaryColumn("Error Rate", rateStatus),
            SummaryColumn("Amplitude", amplitudeStatus),
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

        Grid rateHeader = GaugeHeader("Error Rate (s/d)", "Acceptable band -4 to +6 s/d", out Border rateBandBadge, out TextBlock rateBandText);
        Grid amplitudeHeader = GaugeHeader("Amplitude(°)", "Acceptable band 270 to 300°", out Border amplitudeBandBadge, out TextBlock amplitudeBandText);
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
            rateStatus, amplitudeStatus, elapsedValue, overallText);
        var renderer = new VarioRenderer(
            ratePlot, amplitudePlot, summary,
            new VarioBandBadgeControls(rateBandBadge, rateBandText, amplitudeBandBadge, amplitudeBandText),
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
        double rateMin = VarioGaugePolicy.RateAcceptMinSPerDay;
        double rateMax = VarioGaugePolicy.RateAcceptMaxSPerDay;
        double amplitudeMin = VarioGaugePolicy.AmplitudeAcceptMinDeg;
        double amplitudeMax = VarioGaugePolicy.AmplitudeAcceptMaxDeg;
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
        panel.Children.Add(Title("Error Rate (s/d)"));
        panel.Children.Add(Rule($"Stable · in range: average within {rateMin:0} to {rateMax:0} s/d and σ ≤ {sigma:0}", "VarioGoodBrush"));
        panel.Children.Add(Rule($"In range · unstable: average within {rateMin:0} to {rateMax:0} s/d but σ > {sigma:0}", "VarioWarnBrush"));
        panel.Children.Add(Rule($"Fast / Slow · out of range: average outside {rateMin:0} to {rateMax:0} s/d", "VarioBadBrush"));
        panel.Children.Add(Title("Amplitude(°)"));
        panel.Children.Add(Rule($"Healthy: average {amplitudeMin:0}–{amplitudeMax:0}°", "VarioGoodBrush"));
        panel.Children.Add(Rule($"Slightly low / High: average {service:0}–{amplitudeMin:0}° or above {amplitudeMax:0}°", "VarioWarnBrush"));
        panel.Children.Add(Rule($"Low · service: average below {service:0}°", "VarioBadBrush"));
        return panel;
    }

    private static InfoTabRegistration CreateBeatErrorDiagRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        var tracePlot = new AvaPlot();

        Border alertBanner = CreateAlertBanner(out TextBlock alertText);
        Button resetViewButton = CreateOverlayButton(
            "Reset View", ResetAllGraphViewsTooltip, context.ResetViews.ResetAll);
        resetViewButton.MinHeight = TraceHeaderButtonMinHeight;
        resetViewButton.FontSize = TraceHeaderButtonFontSize;
        resetViewButton.Padding = TraceHeaderButtonPadding;
        resetViewButton.VerticalAlignment = VerticalAlignment.Center;
        resetViewButton.Classes.Add("PositionButton");

        const string zoomActiveClass = "active";
        var zoomButtons = new List<(Button Button, double Factor)>();

        Button ZoomButton(string content, double factor)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 36,
            };
            ToolTip.SetTip(button, $"Show Beat Error slope at {content}");
            zoomButtons.Add((button, factor));
            return button;
        }

        void UpdateZoomButtons(string label)
        {
            foreach ((Button button, double factor) in zoomButtons)
            {
                bool active = label == $"{factor:0}x";
                if (active && !button.Classes.Contains(zoomActiveClass))
                {
                    button.Classes.Add(zoomActiveClass);
                }
                else if (!active)
                {
                    button.Classes.Remove(zoomActiveClass);
                }
            }
        }

        Button zoom1xButton = ZoomButton("1x", 1.0);
        Button zoom2xButton = ZoomButton("2x", 2.0);
        Button zoom4xButton = ZoomButton("4x", 4.0);
        Button zoom8xButton = ZoomButton("8x", 8.0);
        Button zoom16xButton = ZoomButton("16x", 16.0);
        foreach (Button button in zoomButtons.Select(item => item.Button))
        {
            button.MinHeight = TraceHeaderButtonMinHeight;
            button.FontSize = TraceHeaderButtonFontSize;
            button.Padding = TraceHeaderButtonPadding;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Classes.Add("PositionButton");
        }

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
        };
        controls.Children.Add(zoom1xButton);
        controls.Children.Add(zoom2xButton);
        controls.Children.Add(zoom4xButton);
        controls.Children.Add(zoom8xButton);
        controls.Children.Add(zoom16xButton);
        controls.Children.Add(resetViewButton);

        alertBanner.VerticalAlignment = VerticalAlignment.Center;
        alertBanner.Margin = new Thickness(0, 0, 8, 0);
        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };
        Grid.SetColumn(alertBanner, 0);
        Grid.SetColumn(controls, 1);
        headerStrip.Children.Add(alertBanner);
        headerStrip.Children.Add(controls);

        // Numeric panel: label/value cells for the plan readings (error rate,
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

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
        };
        Grid.SetRow(headerStrip, 0);
        Grid.SetRow(readoutGrid, 1);
        Grid.SetRow(tracePlot, 2);
        grid.Children.Add(headerStrip);
        grid.Children.Add(readoutGrid);
        grid.Children.Add(tracePlot);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 2);
            grid.Children.Add(overlay);
        }

        var renderer = new BeatErrorDiagRenderer(
            tracePlot,
            alertBanner,
            alertText,
            valueTexts,
            context.TextFontFamily);
        renderer.SetRateZoomLabelCallback(UpdateZoomButtons);
        foreach ((Button button, double factor) in zoomButtons)
        {
            button.Click += (_, _) => renderer.SetRateZoomFactor(factor);
        }
        context.ResetViews.Register(renderer.ResetView);

        var consumer = new BeatErrorDiagFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }

    private static InfoTabRegistration CreateMultiFilterScopeRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        IReadOnlyList<MultiFilterScopeLane> lanes = MultiFilterScopeLanes.All;
        const int columns = 2;
        int rowPairs = (lanes.Count + columns - 1) / columns;
        var plots = new AvaPlot[lanes.Count];
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto,*", rowPairs))),
        };

        for (int i = 0; i < lanes.Count; i++)
        {
            int col = i % columns;
            int rowPair = i / columns;
            var description = new TextBlock
            {
                Text = lanes[i].Label,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(8, 3, 8, 0),
            };
            plots[i] = new AvaPlot();
            Grid.SetColumn(description, col);
            Grid.SetRow(description, rowPair * 2);
            Grid.SetColumn(plots[i], col);
            Grid.SetRow(plots[i], rowPair * 2 + 1);
            grid.Children.Add(description);
            grid.Children.Add(plots[i]);
        }

        var renderer = new MultiFilterScopeRenderer(plots);
        context.ResetViews.Register(renderer.ResetView);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);
        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            root.Children.Add(overlay);
        }

        Button resetButton = CreateOverlayButton(
            "Reset View", ResetAllGraphViewsTooltip, context.ResetViews.ResetAll);
        resetButton.MinHeight = TraceHeaderButtonMinHeight;
        resetButton.FontSize = TraceHeaderButtonFontSize;
        resetButton.Padding = TraceHeaderButtonPadding;
        resetButton.VerticalAlignment = VerticalAlignment.Center;
        resetButton.Classes.Add("PositionButton");
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        controls.Children.Add(resetButton);
        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };
        Grid.SetColumn(controls, 1);
        headerStrip.Children.Add(controls);
        Grid.SetRow(headerStrip, 0);
        root.Children.Add(headerStrip);

        var consumer = new MultiFilterScopeFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, root), consumer);
    }

    private static InfoTabRegistration CreateLongTermPerfRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // Three stacked panes over compact 24-hour summary/navigation chrome, with
        // a review bar at the bottom. The top strip is touch-friendly for Raspberry
        // Pi use, while the old explanatory legend is folded into the graph styling
        // so the plots keep as much vertical room as possible.
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        // Equal vertical margin total (-8) on every pane so their control heights
        // match; the middle pane previously carried -16 and rendered ~8 px taller.
        ratePlot.Margin = new Thickness(0, 0, 0, -8);
        amplitudePlot.Margin = new Thickness(0, -4, 0, -4);
        beatErrorPlot.Margin = new Thickness(0, -8, 0, 0);

        TextBlock SummaryValue(string text, double fontSize = 14) => new()
        {
            Text = text,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var verdictText = SummaryValue("COLLECTING", 14);
        var rateText = SummaryValue("Error Rate —");
        var amplitudeText = SummaryValue("Amplitude —");
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

        var renderer = new LongTermPerfRenderer(
            ratePlot,
            amplitudePlot,
            beatErrorPlot,
            summaryControls);
        context.ResetViews.Register(renderer.ResetView);

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

        Button resetViewButton = CreateOverlayButton(
            "Reset View", ResetAllGraphViewsTooltip, context.ResetViews.ResetAll);
        resetViewButton.MinHeight = TraceHeaderButtonMinHeight;
        resetViewButton.FontSize = TraceHeaderButtonFontSize;
        resetViewButton.Padding = TraceHeaderButtonPadding;
        resetViewButton.VerticalAlignment = VerticalAlignment.Center;
        resetViewButton.Classes.Add("PositionButton");
        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerButtons.Children.Add(navigation);
        headerButtons.Children.Add(resetViewButton);

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
            ClipToBounds = true,
        };
        Grid.SetColumn(summaryRow, 0);
        Grid.SetColumn(headerButtons, 1);
        headerGrid.Children.Add(summaryRow);
        headerGrid.Children.Add(headerButtons);

        // The beat-error pane shows the shared time axis (~41 px taller axis panel
        // than the two upper panes, whose X axis is hidden), so its row is enlarged
        // to keep all three DATA areas the same height. Tuned for the 1280x750
        // design size; a large window resize drifts it by a few px.
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,*,1.22*,Auto"),
        };
        Border reviewBar = CreateLongTermReviewBar();
        Control[] rows = { headerGrid, ratePlot, amplitudePlot, beatErrorPlot, reviewBar };
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

    private static InfoTabRegistration CreateWatchPositionsRegistration(
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
            buttons[(int)position] = button;
            buttonGrid.Children.Add(button);
        }

        var initialPosition = (WatchPosition)(context.ViewModel?.SelectedPositionIndex ?? 0);
        var diagram = new WatchModelView
        {
            Position = initialPosition,
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ToolTip.SetTip(diagram, "Active watch position orientation");
        var positionRenderer = new WatchPositionsRenderer(buttons, diagram, initialPosition);

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
        var dashboardControls = new PositionSequenceDashboardControls(
            activePositionText,
            activeOrientationText);

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("276,*"),
            RowDefinitions = new RowDefinitions("*"),
            VerticalAlignment = VerticalAlignment.Top,
            MaxHeight = 366,
        };
        Grid.SetColumn(activePanel, 0);
        Grid.SetColumn(tableGrid, 1);
        topGrid.Children.Add(activePanel);
        topGrid.Children.Add(tableGrid);

        var sequenceGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*"),
            Margin = new Thickness(4, 4, 8, 4),
        };
        Grid.SetRow(topGrid, 0);
        sequenceGrid.Children.Add(topGrid);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            sequenceGrid.Children.Add(overlay);
        }

        var sequenceRenderer = new MultiPositionSeqRenderer(
            tableGrid,
            dashboardControls,
            initialPosition);
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
                sequenceRenderer.RequestPosition(position);
            };
        }

        var consumer = new WatchPositionsFrameConsumer(positionRenderer, sequenceRenderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, sequenceGrid), consumer);
    }

    private static Border CreateActivePositionPanel(
        WatchModelView diagram,
        out TextBlock activePositionText,
        out TextBlock activeOrientationText)
    {
        activePositionText = new TextBlock
        {
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        activeOrientationText = new TextBlock
        {
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        // The model fills the middle (* row) so it is as large as the panel
        // allows; the position labels are pinned to the bottom Auto row.
        var labels = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { activePositionText, activeOrientationText },
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        TextBlock header = CreatePositionSectionHeader("ACTIVE");
        Grid.SetRow(header, 0);
        Grid.SetRow(diagram, 1);
        Grid.SetRow(labels, 2);
        layout.Children.Add(header);
        layout.Children.Add(diagram);
        layout.Children.Add(labels);

        return new Border
        {
            Classes = { "PositionPanel" },
            Padding = new Thickness(10, 8),
            Margin = new Thickness(4, 0, 10, 0),
            Child = layout,
        };
    }

    private static TextBlock CreatePositionSectionHeader(string text) => new()
    {
        Text = text,
        FontSize = PositionMinimumFontSize,
        Opacity = 0.75,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
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

        var renderer = new BeatNoiseScopeRenderer(
            mainPlot,
            stripPlot,
            averagePlot,
            liftText,
            averageText,
            context.ViewModel?.UseCOnset == true);
        if (context.ViewModel is { } beatNoiseViewModel)
        {
            beatNoiseViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.UseCOnset))
                {
                    renderer.SetUseCOnset(beatNoiseViewModel.UseCOnset);
                }
            };
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Button BeatNoiseHeaderButton(string content, string tooltip)
        {
            var button = new Button
            {
                Content = content,
                MinHeight = TraceHeaderButtonMinHeight,
                Padding = TraceHeaderButtonPadding,
                FontSize = TraceHeaderButtonFontSize,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(button, tooltip);
            button.Classes.Add("PositionButton");
            return button;
        }

        ToggleButton BeatNoiseHeaderToggle(string content, string tooltip)
        {
            var toggle = new ToggleButton
            {
                Content = content,
                MinHeight = TraceHeaderButtonMinHeight,
                Padding = TraceHeaderButtonPadding,
                FontSize = TraceHeaderButtonFontSize,
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.Classes.Add("PositionButton");
            ToolTip.SetTip(toggle, tooltip);
            return toggle;
        }

        Button envelopeModeButton = BeatNoiseHeaderButton(
            "Beat Scope",
            "Show beat-noise waveform + Strip mode (absolute-value display controlled separately)");
        buttonRow.Children.Add(envelopeModeButton);

        Button averageModeButton = BeatNoiseHeaderButton(
            "Avg Envelope",
            "Show Average Envelope + Strip mode");
        buttonRow.Children.Add(averageModeButton);

        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };

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
            Button button = BeatNoiseHeaderButton(
                rangeMs + " ms",
                $"Show the first {rangeMs} ms of the beat window");
            button.Click += (_, _) =>
            {
                renderer.SetRangeMs(rangeMs);
                UpdateRangeButtonStates();
            };
            rangeButtons[i] = button;
            buttonRow.Children.Add(button);
        }

        UpdateRangeButtonStates();

        ToggleButton absoluteToggle = BeatNoiseHeaderToggle(
            "ABS",
            "On: show rectified absolute-value envelope. Off: show real bipolar waveform (min/max).");
        absoluteToggle.IsCheckedChanged += (_, _) => renderer.SetAbsoluteValue(absoluteToggle.IsChecked == true);
        buttonRow.Children.Add(absoluteToggle);

        // Σ writes the shared SigmaAveraging view-model property; MainWindow
        // forwards the change to the running analysis worker (the
        // SetSweepMultiple flow). Display state comes back via the snapshot.
        ToggleButton sigmaToggle = BeatNoiseHeaderToggle(
            "Σ",
            "Average 50 + 50 beat noises into the two Scope 2 traces");
        sigmaToggle.IsChecked = context.ViewModel?.SigmaAveraging == true;
        sigmaToggle.IsCheckedChanged += (_, _) =>
        {
            if (context.ViewModel is { } viewModel)
            {
                viewModel.SigmaAveraging = sigmaToggle.IsChecked == true;
            }
        };
        buttonRow.Children.Add(sigmaToggle);

        buttonRow.Children.Add(liftText);
        Grid.SetColumn(buttonRow, 1);
        headerStrip.Children.Add(buttonRow);

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
        Control[] rows = { headerStrip, mainPlot, stripPlot, averageText };
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
        // Header numeric line (error rate / beat error / BPH) above one plot that
        // stacks the recent beats in A-aligned, peak-normalized lanes, and a
        // one-line legend for the guide markers below.
        var headerText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(8, 4, 8, 2),
        };
        var lanePlot = new AvaPlot();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        Grid.SetRow(headerText, 0);
        Grid.SetRow(lanePlot, 1);
        grid.Children.Add(headerText);
        grid.Children.Add(lanePlot);

        if (CreateWaitingOverlay(context.ViewModel) is { } overlay)
        {
            Grid.SetRow(overlay, 1);
            grid.Children.Add(overlay);
        }

        var renderer = new WaveformCompareRenderer(lanePlot, headerText, context.TextFontFamily);
        var consumer = new WaveformCompareFrameConsumer(renderer);

        lanePlot.PointerPressed += (_, e) =>
        {
            if (lanePlot.Bounds.Height > 0)
            {
                renderer.SelectPairAtPixelY(e.GetPosition(lanePlot).Y);
            }
        };

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
