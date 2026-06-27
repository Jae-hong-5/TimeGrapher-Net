using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Analysis;

namespace TimeGrapher.App.Tabs;

internal sealed partial class InfoTabRegistry
{
    private static InfoTabRegistration CreateSpectrogramRegistration(
        InfoTabDefinition definition,
        InfoTabFactoryContext context)
    {
        // Core-built STFT image (x = time, y = frequency, color = dB intensity):
        // frequency-axis labels on the left, the dB colorbar legend drawn
        // vertically on the right (aligned to the y-axis), and the time-axis
        // caption below. The spectrogram shows raw signal energy before beat
        // sync, so no waiting overlay is added (the Filter Scope reasoning).
        _ = context;
        var image = new Image
        {
            Stretch = Stretch.Fill,
        };

        // Themed backdrop behind the image so the graph area reads as the scope
        // background (white light / black dark) even before the first run paints
        // an image — otherwise the null-source Image shows the window color and
        // the graph area is invisible. It also matches the spectrogram dB floor.
        var imageHost = new Border { Child = image };
        imageHost.Bind(Border.BackgroundProperty, imageHost.GetResourceObservable("ScopeBgBrush"));

        TextBlock Label(string text) => new()
        {
            Text = text,
            FontSize = 11,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Themed tick mark (matches the label color/opacity) for an axis strip.
        Rectangle Tick(double width, double height, HorizontalAlignment ha, VerticalAlignment va)
        {
            var tick = new Rectangle
            {
                Width = width,
                Height = height,
                HorizontalAlignment = ha,
                VerticalAlignment = va,
                Opacity = 0.65,
            };
            tick.Bind(Shape.FillProperty, tick.GetResourceObservable("TextPrimaryBrush"));
            return tick;
        }

        // Frequency axis: evenly spaced label + tick slots, low at the bottom.
        // The Hz values span 0..Nyquist (sampleRate / 2) and depend on the run's
        // sample rate, so the renderer fills them once a frame arrives; the slots
        // are created empty here. Labels and ticks sit at the top edge of
        // (count-1) equal rows so each lands on its exact frequency fraction (the
        // last at the bottom edge). The ticks live in a thin strip beside the labels.
        const int freqTickCount = 7;
        string freqRows = string.Join(",", Enumerable.Repeat("*", freqTickCount - 1));
        var axisGrid = new Grid
        {
            Margin = new Thickness(8, 2, 2, 2),
            RowDefinitions = new RowDefinitions(freqRows),
        };
        var freqTickStrip = new Grid { RowDefinitions = new RowDefinitions(freqRows) };
        var freqLabels = new TextBlock[freqTickCount];
        for (int i = 0; i < freqTickCount; i++)
        {
            bool last = i == freqTickCount - 1;
            int row = last ? freqTickCount - 2 : i;
            VerticalAlignment va = last ? VerticalAlignment.Bottom : VerticalAlignment.Top;

            TextBlock label = Label(string.Empty);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = va;
            Grid.SetRow(label, row);
            freqLabels[i] = label;
            axisGrid.Children.Add(label);

            Rectangle tick = Tick(6, 1, HorizontalAlignment.Right, va);
            Grid.SetRow(tick, row);
            freqTickStrip.Children.Add(tick);
        }

        // dB colorbar: a vertical gradient (dB ceiling at the top, dB floor at the
        // bottom) with a tick + label every 10 dB beside it, aligned to the
        // image's y-axis height the same way the frequency axis is.
        var legendImage = new Image
        {
            Stretch = Stretch.Fill,
            Width = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        const double dbStep = 10.0;
        int dbTickCount = (int)((SpectrogramFrameProjector.DbCeiling - SpectrogramFrameProjector.DbFloor) / dbStep) + 1;
        var colorbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("*", dbTickCount - 1))),
            Margin = new Thickness(6, 2, 8, 2),
        };
        Grid.SetColumn(legendImage, 0);
        Grid.SetRowSpan(legendImage, dbTickCount - 1);
        colorbar.Children.Add(legendImage);
        for (int i = 0; i < dbTickCount; i++)
        {
            double db = SpectrogramFrameProjector.DbCeiling - i * dbStep; // ceiling at the top down to the floor
            bool last = i == dbTickCount - 1;
            int row = last ? dbTickCount - 2 : i;
            VerticalAlignment va = last ? VerticalAlignment.Bottom : VerticalAlignment.Top;

            Rectangle tick = Tick(5, 1, HorizontalAlignment.Left, va);
            Grid.SetColumn(tick, 1);
            Grid.SetRow(tick, row);
            colorbar.Children.Add(tick);

            TextBlock label = Label($"{db:0} dB");
            label.VerticalAlignment = va;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.Margin = new Thickness(3, 0, 0, 0);
            Grid.SetColumn(label, 2);
            Grid.SetRow(label, row);
            colorbar.Children.Add(label);
        }

        // Time axis: six evenly spaced ticks; the labels' values are filled by the
        // renderer to match the selected window (Last Beat / Seconds), so they are
        // created empty here. Ticks sit in a thin strip below the image; the
        // caption (also renderer-filled) names the unit and window.
        const int timeTickCount = 6;
        string timeCols = string.Join(",", Enumerable.Repeat("*", timeTickCount - 1));
        var timeTickStrip = new Grid
        {
            Height = 6,
            ColumnDefinitions = new ColumnDefinitions(timeCols),
        };
        var timeLabelGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(timeCols),
            Margin = new Thickness(0, 1, 0, 0),
        };
        var timeLabels = new TextBlock[timeTickCount];
        for (int i = 0; i < timeTickCount; i++)
        {
            bool last = i == timeTickCount - 1;
            int col = last ? timeTickCount - 2 : i;
            HorizontalAlignment ha = last ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            Rectangle tick = Tick(1, 6, ha, VerticalAlignment.Top);
            Grid.SetColumn(tick, col);
            timeTickStrip.Children.Add(tick);

            TextBlock label = Label(string.Empty);
            label.HorizontalAlignment = ha;
            Grid.SetColumn(label, col);
            timeLabels[i] = label;
            timeLabelGrid.Children.Add(label);
        }

        // Compare Beats needs each beat to read 0 at its own left edge, which the
        // fixed six-tick grid above cannot express (its ticks fall at fractions of
        // the whole strip, not on the beat boundaries). Beats mode therefore hides
        // that grid and draws ticks/labels onto this overlay canvas instead, placing
        // a nice-step 0-based ruler per lane from the live beat width. The pool is
        // sized for the densest case (8 beats × several ticks each, plus the max).
        const int beatRulerCapacity = 72;
        var beatRulerCanvas = new Canvas { Height = 22, IsVisible = false, ClipToBounds = true };
        var beatRulerTicks = new Rectangle[beatRulerCapacity];
        var beatRulerLabels = new TextBlock[beatRulerCapacity];
        for (int i = 0; i < beatRulerCapacity; i++)
        {
            Rectangle tick = Tick(1, 6, HorizontalAlignment.Left, VerticalAlignment.Top);
            tick.IsVisible = false;
            Canvas.SetTop(tick, 0);
            beatRulerTicks[i] = tick;
            beatRulerCanvas.Children.Add(tick);

            TextBlock label = Label(string.Empty);
            label.Width = 34;
            label.IsVisible = false;
            Canvas.SetTop(label, 7);
            beatRulerLabels[i] = label;
            beatRulerCanvas.Children.Add(label);
        }
        var timeAxisCaption = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.65,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 4),
        };

        // Edge-to-edge direction indicator for Compare Beats, mirroring the
        // Comparison tab's full-length arrow: "Past" pinned to the left (oldest beat)
        // and "Current" to the right (newest beat) with a stretched accent arrow
        // spanning the whole time axis between them. The renderer shows it only in
        // Beats mode.
        var beatDirectionStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(8, 0, 8, 4),
            IsVisible = false,
        };
        var beatDirPast = new TextBlock
        {
            Text = "Past",
            FontSize = 11,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // A drawn arrow (triangular head + shaft) reads cleaner than a glyph: the
        // head is a left-pointing triangle pinned to the left, the shaft a thin
        // rounded bar stretching to the right edge. Both share the accent fill and
        // the same vertical centre so they join seamlessly.
        var beatDirArrow = new Grid { Margin = new Thickness(6, 0, 6, 0), MinHeight = 10 };
        var beatDirLine = new Rectangle
        {
            Height = 2,
            RadiusX = 1,
            RadiusY = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
        };
        beatDirLine.Bind(Shape.FillProperty, beatDirLine.GetResourceObservable("ChromeAccentBrush"));
        var beatDirHead = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M 0,5 L 10,0 L 10,10 Z"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        beatDirHead.Bind(Shape.FillProperty, beatDirHead.GetResourceObservable("ChromeAccentBrush"));
        beatDirArrow.Children.Add(beatDirLine);
        beatDirArrow.Children.Add(beatDirHead);
        var beatDirCurrent = new TextBlock
        {
            Text = "Current",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        beatDirCurrent.Bind(TextBlock.ForegroundProperty, beatDirCurrent.GetResourceObservable("ChromeAccentBrush"));
        Grid.SetColumn(beatDirPast, 0);
        Grid.SetColumn(beatDirArrow, 1);
        Grid.SetColumn(beatDirCurrent, 2);
        beatDirectionStrip.Children.Add(beatDirPast);
        beatDirectionStrip.Children.Add(beatDirArrow);
        beatDirectionStrip.Children.Add(beatDirCurrent);

        // Live-head marker: a thin red vertical line the renderer slides to the
        // sweep head, showing where data is being written right now. It overlays
        // the image (added to the same cell after it) and ignores pointer input
        // so the wheel still reaches the graph.
        var currentLine = new Rectangle
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        // Live-head color flows from App.axaml like the themed tick fills above
        // (never hardcoded); ChromeAccentBrush is the theme's red accent and
        // adapts to the light/dark variant.
        currentLine.Bind(Shape.FillProperty, currentLine.GetResourceObservable("ChromeAccentBrush"));

        // Beats-mode lane dividers: solid vertical bars the renderer positions over
        // the beat boundaries so each beat reads as its own cell. They overlay the
        // image like the live-head marker (vector, so they stay crisp instead of
        // blurring like a line baked into the stretched bitmap) and are painted the
        // surrounding surface color. One per possible boundary — the largest beat
        // count on the ladder below is 8, so 7 dividers cover every count.
        const int maxBeatDividers = 7;
        var beatDividers = new Rectangle[maxBeatDividers];
        for (int i = 0; i < maxBeatDividers; i++)
        {
            var divider = new Rectangle
            {
                Width = SpectrogramRenderer.BeatDividerWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
                IsVisible = false,
            };
            divider.Bind(Shape.FillProperty, divider.GetResourceObservable("SurfaceBgBrush"));
            beatDividers[i] = divider;
        }

        var renderer = new SpectrogramRenderer(image, legendImage, freqLabels, timeLabels, timeTickStrip, timeLabelGrid, timeAxisCaption, beatDirectionStrip, beatRulerCanvas, beatRulerTicks, beatRulerLabels, currentLine, beatDividers);

        double[] secondsLadder = { 0.5, 1.0, 2.0, 5.0, 10.0 };
        int secondsIndex = 1; // 1.0 s, matching the SpectrogramRenderer default
        // Beat-count ladder for Beats mode: 2 (compare one beat with the next) up to
        // 8 (recurring per-beat energy structure across several beats).
        int[] beatsLadder = { 2, 4, 8 };
        int beatsIndex = 0; // 2 beats, matching the SpectrogramRenderer default
        var viewMode = SpectrogramViewMode.Seconds;

        Button ToolbarButton(string content, string tooltip)
        {
            var button = new Button { Content = content };
            ToolTip.SetTip(button, tooltip);
            button.MinHeight = TraceHeaderButtonMinHeight;
            button.Height = TraceHeaderButtonMinHeight;
            button.MinWidth = 36;
            button.FontSize = TraceHeaderButtonFontSize;
            button.Padding = TraceHeaderButtonPadding;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Classes.Add("PositionButton");
            return button;
        }
        Button lastBeatButton = ToolbarButton("Last Beat", "Show the most recent single beat period");
        Button beatsButton = ToolbarButton("Compare Beats", "Show the last few beats side by side to compare one with the next");
        Button secondsButton = ToolbarButton("Seconds", "Show a fixed number of seconds");
        Button minusButton = ToolbarButton("−", "Shorter window / fewer beats");
        Button plusButton = ToolbarButton("+", "Longer window / more beats");
        var secondsText = new TextBlock
        {
            FontSize = TraceHeaderButtonFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var secondsReadout = new Border
        {
            Height = TraceHeaderButtonMinHeight,
            MinHeight = TraceHeaderButtonMinHeight,
            MinWidth = 44,
            Margin = new Thickness(2, 0, 2, 0),
            Child = secondsText,
        };

        void UpdateToolbar()
        {
            SetActiveButtonClass(lastBeatButton, viewMode == SpectrogramViewMode.LastBeat);
            SetActiveButtonClass(beatsButton, viewMode == SpectrogramViewMode.Beats);
            SetActiveButtonClass(secondsButton, viewMode == SpectrogramViewMode.Seconds);

            bool seconds = viewMode == SpectrogramViewMode.Seconds;
            bool beats = viewMode == SpectrogramViewMode.Beats;
            // The −/+ stepper and the readout drive whichever ladder the active mode
            // owns (seconds in Seconds mode, beat count in Beats mode); both are
            // inert in Last Beat, which has a single fixed window.
            minusButton.IsEnabled = (seconds && secondsIndex > 0) || (beats && beatsIndex > 0);
            plusButton.IsEnabled = (seconds && secondsIndex < secondsLadder.Length - 1)
                || (beats && beatsIndex < beatsLadder.Length - 1);
            secondsText.Opacity = seconds || beats ? 1.0 : 0.4;
            secondsText.Text = beats ? $"{beatsLadder[beatsIndex]} beats" : $"{secondsLadder[secondsIndex]:0.#} s";
        }

        // Steps the active mode's ladder (shared by the −/+ buttons and the
        // wheel-over-graph gesture): the seconds window in Seconds mode, the beat
        // count in Beats mode. No-op in Last Beat or at the ladder ends.
        void StepWindow(int delta)
        {
            if (viewMode == SpectrogramViewMode.Seconds)
            {
                int next = Math.Clamp(secondsIndex + delta, 0, secondsLadder.Length - 1);
                if (next != secondsIndex)
                {
                    secondsIndex = next;
                    renderer.SetViewSeconds(secondsLadder[secondsIndex]);
                    UpdateToolbar();
                }
            }
            else if (viewMode == SpectrogramViewMode.Beats)
            {
                int next = Math.Clamp(beatsIndex + delta, 0, beatsLadder.Length - 1);
                if (next != beatsIndex)
                {
                    beatsIndex = next;
                    renderer.SetViewBeats(beatsLadder[beatsIndex]);
                    UpdateToolbar();
                }
            }
        }

        lastBeatButton.Click += (_, _) =>
        {
            viewMode = SpectrogramViewMode.LastBeat;
            renderer.SetViewMode(viewMode);
            UpdateToolbar();
        };
        beatsButton.Click += (_, _) =>
        {
            viewMode = SpectrogramViewMode.Beats;
            renderer.SetViewBeats(beatsLadder[beatsIndex]);
            renderer.SetViewMode(viewMode);
            UpdateToolbar();
        };
        secondsButton.Click += (_, _) =>
        {
            viewMode = SpectrogramViewMode.Seconds;
            renderer.SetViewMode(viewMode);
            UpdateToolbar();
        };
        minusButton.Click += (_, _) => StepWindow(-1);
        plusButton.Click += (_, _) => StepWindow(+1);

        // Wheel over the graph mirrors the −/+ buttons: up lengthens the window
        // (longer time interval, the + step), down shortens it (the − step).
        imageHost.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y > 0)
            {
                StepWindow(+1);
            }
            else if (e.Delta.Y < 0)
            {
                StepWindow(-1);
            }

            e.Handled = true;
        };
        UpdateToolbar();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.Children.Add(lastBeatButton);
        toolbar.Children.Add(beatsButton);
        toolbar.Children.Add(secondsButton);
        toolbar.Children.Add(minusButton);
        toolbar.Children.Add(secondsReadout);
        toolbar.Children.Add(plusButton);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
        };
        var headerStrip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 1, 8, 2),
        };
        Grid.SetColumn(toolbar, 1);
        headerStrip.Children.Add(toolbar);
        Grid.SetRow(headerStrip, 0);
        Grid.SetColumn(headerStrip, 0);
        Grid.SetColumnSpan(headerStrip, 4);
        Grid.SetRow(axisGrid, 1);
        Grid.SetColumn(axisGrid, 0);
        Grid.SetRow(freqTickStrip, 1);
        Grid.SetColumn(freqTickStrip, 1);
        Grid.SetRow(imageHost, 1);
        Grid.SetColumn(imageHost, 2);
        Grid.SetRow(currentLine, 1);
        Grid.SetColumn(currentLine, 2);
        Grid.SetRow(colorbar, 1);
        Grid.SetColumn(colorbar, 3);
        Grid.SetRow(timeTickStrip, 2);
        Grid.SetColumn(timeTickStrip, 2);
        Grid.SetRow(timeLabelGrid, 3);
        Grid.SetColumn(timeLabelGrid, 2);
        Grid.SetRow(timeAxisCaption, 4);
        Grid.SetColumn(timeAxisCaption, 2);
        Grid.SetRow(beatRulerCanvas, 2);
        Grid.SetRowSpan(beatRulerCanvas, 2);
        Grid.SetColumn(beatRulerCanvas, 2);
        Grid.SetRow(beatDirectionStrip, 5);
        Grid.SetColumn(beatDirectionStrip, 2);
        grid.Children.Add(headerStrip);
        grid.Children.Add(axisGrid);
        grid.Children.Add(freqTickStrip);
        grid.Children.Add(imageHost);
        grid.Children.Add(currentLine); // after imageHost so it overlays the image
        foreach (Rectangle divider in beatDividers)
        {
            Grid.SetRow(divider, 1);
            Grid.SetColumn(divider, 2);
            grid.Children.Add(divider); // same cell as the image, overlaid on top
        }
        grid.Children.Add(colorbar);
        grid.Children.Add(timeTickStrip);
        grid.Children.Add(timeLabelGrid);
        grid.Children.Add(timeAxisCaption);
        grid.Children.Add(beatRulerCanvas); // overlays the time-axis rows in Beats mode
        grid.Children.Add(beatDirectionStrip);
        var consumer = new SpectrogramFrameConsumer(renderer);
        return new InfoTabRegistration(definition, CreateTabItem(definition, grid), consumer);
    }
}
