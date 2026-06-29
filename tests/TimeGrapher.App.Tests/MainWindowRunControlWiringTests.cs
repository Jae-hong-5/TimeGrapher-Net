using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.App.Views;
using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MainWindowRunControlWiringTests
{
    [Fact]
    public void RunControlButtonsBindToTheDocumentedCommands()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement stopButton = FindNamedElement(document, "StopPushButton");
        XElement playPauseButton = FindNamedElement(document, "PlayPausePushButton");

        // The wiring contract is the Command/IsEnabled bindings plus the accessible
        // name/tooltip; the icon geometry and pixel widths are presentation details
        // that change on benign restyling and carry no behavioral stake, so they are
        // not asserted here.
        Assert.Equal("{Binding StopCommand}", stopButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsStopEnabled}", stopButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("Stop", stopButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Stop current run", stopButton.Attribute("ToolTip.Tip")?.Value);

        Assert.Equal("{Binding PlayPauseCommand}", playPauseButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsPlayPauseEnabled}", playPauseButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding PlayPauseButtonText}", playPauseButton.Attribute("ToolTip.Tip")?.Value);
    }

    [Fact]
    public void BuildRunSettingsWiresSpuriousBeatRejectionFromViewModel()
    {
        // F6: the spurious-beat toggle must reach AnalysisRunSettings (and thence the
        // acquisition gate fraction) through BuildRunSettings. The ViewModel<->XAML
        // binding and the AnalysisRunSettings->gate-fraction mapping are tested
        // elsewhere; this guards the BuildRunSettings call-site argument from being
        // dropped or inverted (which neither of those tests would catch).
        string source = File.ReadAllText(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml.cs"));
        Assert.Contains("SpuriousBeatRejection: mViewModel.SpuriousBeatRejection", source);
    }

    // The rescue-strength step normalization is covered behaviorally by
    // AnalysisRunSettingsTests.WeakAOnsetRescueStrengthStep_ClampsAtTheDetectorBoundary
    // (which drives ToWorkerConfig); a brittle source-text grep for the call string
    // added no protection beyond that, so it was removed.

    [Fact]
    public void RunControlSurfaceDoesNotExposeLegacyResetControls()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        Assert.DoesNotContain(
            document.Descendants().Attributes("Command").Select(attribute => attribute.Value),
            value => value.Contains("ResetCommand", StringComparison.Ordinal) ||
                value.Contains("ResetSequenceCommand", StringComparison.Ordinal));
        Assert.DoesNotContain(
            document.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            value => value.Contains("ResetPushButton", StringComparison.Ordinal) ||
                value.Contains("ResetSequence", StringComparison.Ordinal));
    }

    [Fact]
    public void StatusBarWarningClassPromotesTheWholeBottomBar()
    {
        XDocument mainWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));
        XDocument app = XDocument.Load(FindSourceFile("src/TimeGrapher.App/App.axaml"));

        XElement statusBar = FindNamedElement(mainWindow, "StatusBar");
        XElement statusText = FindNamedElement(mainWindow, "StatusBarText");
        XElement positionText = FindNamedElement(mainWindow, "PositionBarText");
        XElement latencyText = FindNamedElement(mainWindow, "LatencyBarText");

        Assert.Equal("StatusBar", statusBar.Attribute("Classes")?.Value);
        Assert.Equal("{Binding IsStatusWarning}", statusBar.Attribute("Classes.status-warning")?.Value);
        Assert.Equal("StatusBarReadout", statusText.Attribute("Classes")?.Value);
        Assert.Equal("StatusBarReadout position", positionText.Attribute("Classes")?.Value);
        Assert.Equal("StatusBarReadout", latencyText.Attribute("Classes")?.Value);
        Assert.DoesNotContain(mainWindow.Descendants(), element =>
            element.Attribute("IsVisible")?.Value == "{Binding IsStatusWarning}");
        Assert.DoesNotContain(mainWindow.Descendants().Attributes("Classes"),
            attribute => attribute.Value == "GraphWarningOverlay");

        Assert.Contains(app.Descendants(), element =>
            element.Attribute("Selector")?.Value == "Border.StatusBar.status-warning" &&
            element.Descendants().Any(setter =>
                setter.Attribute("Property")?.Value == "Background" &&
                setter.Attribute("Value")?.Value == "{DynamicResource ChromeAccentBrush}"));
        Assert.Contains(app.Descendants(), element =>
            element.Attribute("Selector")?.Value == "Border.StatusBar.status-warning TextBlock.StatusBarReadout" &&
            element.Descendants().Any(setter =>
                setter.Attribute("Property")?.Value == "Foreground" &&
                setter.Attribute("Value")?.Value == "White"));
    }

    [Fact]
    public void ResetOperationResetsGraphDataAndViews()
    {
        string source = File.ReadAllText(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml.cs"));
        int dataReset = source.IndexOf(
            "mGraphFrameRenderer.Reset(BuildTabResetContext());",
            StringComparison.Ordinal);
        int viewReset = source.IndexOf(
            "mInfoTabRegistry.ResetViews.ResetAll();",
            StringComparison.Ordinal);

        Assert.True(dataReset >= 0);
        Assert.True(viewReset > dataReset);
    }

    [Fact]
    public void EofCompletionKeepsFinalRenderAheadOfTerminalRunState()
    {
        string source = File.ReadAllText(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.RunLifecycle.cs"));

        int finalFrameFlag = source.IndexOf(
            "bool finalFrameQueued = analysisOutcome == RunSessionStopOutcome.Stopped;",
            StringComparison.Ordinal);
        int delayedFailure = source.IndexOf(
            "Dispatcher.UIThread.Post(finishFailed);",
            StringComparison.Ordinal);
        int delayedSuccess = source.IndexOf(
            "Dispatcher.UIThread.Post(() => FinishCompletedPlaybackOrSimulationRun(",
            StringComparison.Ordinal);
        int incompleteWarning = source.IndexOf(
            "UserErrorMessages.MeasurementLogMayBeIncomplete",
            StringComparison.Ordinal);

        Assert.True(finalFrameFlag >= 0);
        Assert.True(delayedFailure > finalFrameFlag);
        Assert.True(delayedSuccess > finalFrameFlag);
        Assert.True(incompleteWarning > delayedSuccess);
    }

    [Fact]
    public void InputDeviceComboBoxReloadsDevicesWhenDropDownOpens()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement inputComboBox = FindNamedElement(document, "InputDeviceComboBox");

        Assert.Equal(
            "OnInputDeviceComboBoxDropDownOpened",
            inputComboBox.Attribute("DropDownOpened")?.Value);
    }

    [Fact]
    public void RunStartInputsLiveInSettingsRunParameters()
    {
        XDocument mainWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));
        XDocument settingsWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/SettingsWindow.axaml"));

        XElement averagingPeriodInput = FindNamedElement(settingsWindow, "AveragingPeriodSpinBox");
        XElement highPassInput = FindNamedElement(settingsWindow, "HighLineEdit");

        Assert.Equal("NumericUpDown", averagingPeriodInput.Name.LocalName);
        Assert.Equal("{Binding AreRunParametersEnabled}", averagingPeriodInput.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding AveragingPeriod, Mode=TwoWay}", averagingPeriodInput.Attribute("Value")?.Value);
        Assert.Equal("1", averagingPeriodInput.Attribute("Minimum")?.Value);
        Assert.Equal("240", averagingPeriodInput.Attribute("Maximum")?.Value);
        Assert.Equal("1", averagingPeriodInput.Attribute("Increment")?.Value);

        Assert.Equal("TextBox", highPassInput.Name.LocalName);
        Assert.Equal("{Binding AreRunParametersEnabled}", highPassInput.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding HighPassCutoffText, Mode=TwoWay}", highPassInput.Attribute("Text")?.Value);
        Assert.DoesNotContain(
            mainWindow.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            value => value is "AveragingPeriodComboBox" or "AveragingPeriodSpinBox" or "AveragingPeriodLabel" or
                "MiscLabel" or "HighPassLabel" or "HighLineEdit");
    }

    [Fact]
    public void SettingsRunParametersRenderWithAveragingPeriodInput()
    {
        HeadlessPlatform.EnsureStarted();

        var window = new SettingsWindow
        {
            DataContext = new MainWindowViewModel(),
            Width = 760,
            Height = 600,
        };
        Control content = Assert.IsAssignableFrom<Control>(window.Content);
        content.Measure(new Size(760, 600));
        content.Arrange(new Rect(0, 0, 760, 600));

        NumericUpDown averagingPeriod = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("AveragingPeriodSpinBox"));
        NumericUpDown blockSize = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("AnalysisBlockSizeSpinBox"));
        NumericUpDown captureBuffer = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("CaptureBufferMsSpinBox"));
        TextBox highPass = Assert.IsType<TextBox>(
            window.FindControl<Control>("HighLineEdit"));
        Slider rescueStrength = Assert.IsType<Slider>(
            window.FindControl<Control>("WeakAOnsetRescueStrengthSlider"));
        NumericUpDown verdictBeats = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("VerdictMinimumBeatsSpinBox"));

        Rect averagingPeriodBounds = BoundsInContent(averagingPeriod, content);
        Rect blockSizeBounds = BoundsInContent(blockSize, content);
        Rect captureBufferBounds = BoundsInContent(captureBuffer, content);
        Rect rescueStrengthBounds = BoundsInContent(rescueStrength, content);
        Rect highPassBounds = BoundsInContent(highPass, content);
        Rect verdictBeatsBounds = BoundsInContent(verdictBeats, content);

        Assert.True(averagingPeriodBounds.Width > 0);
        Assert.True(blockSizeBounds.Width > 0);
        Assert.True(captureBufferBounds.Width > 0);
        Assert.True(rescueStrengthBounds.Width > 0);
        Assert.True(rescueStrengthBounds.Bottom <= 600);
        Assert.True(highPassBounds.Width > 0);
        Assert.True(highPassBounds.Bottom <= 600);
        Assert.True(verdictBeatsBounds.Width > 0);
        Assert.True(verdictBeatsBounds.Bottom <= 600);

        var target = new RenderTargetBitmap(new PixelSize(760, 600), new Vector(96, 96));
        target.Render(content);

        Assert.True(CountOpaquePixels(target, 760, 600) > 10000);
    }

    [Fact]
    public void ReloadedInputDeviceSelectionKeepsCurrentSourceWhenPresent()
    {
        string[] names = { "Live: Welshi USB", "Playback", "Simulation" };

        Assert.Equal(1, MainWindow.SelectInputDeviceIndexAfterReload(names, "Playback"));
        Assert.Equal(2, MainWindow.SelectInputDeviceIndexAfterReload(names, "Simulation"));
    }

    [Fact]
    public void ReloadedInputDeviceSelectionFallsBackToPreferredDevice()
    {
        string[] names = { "Live: Chinese Generic USB", "Playback", "Simulation" };

        Assert.Equal(0, MainWindow.SelectInputDeviceIndexAfterReload(names, "Missing device"));
    }

    [Fact]
    public void SavedBphSelectionRestoresPersistedCatalogIndex()
    {
        int index = MainWindow.SavedCatalogIndexOrFallback(
            BphCatalog.ManualAutoBph,
            savedValue: 21600,
            fallbackIndex: 0);

        Assert.Equal(RunSelectionResolver.FindValue(BphCatalog.ManualAutoBph, 21600), index);
    }

    [Fact]
    public void SavedSimulationBphSelectionFallsBackToDefaultIndex()
    {
        int index = MainWindow.SavedCatalogIndexOrFallback(
            BphCatalog.ManualBph,
            savedValue: -1,
            fallbackIndex: 3);

        Assert.Equal(3, index);
    }

    [Fact]
    public void TitleBarPlacesAiBeforeThemeHelpAndSettings()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        string?[] titleBarButtonNames = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Name")?.Value)
            .Where(name => name is not null)
            .Take(7)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AiAnalysisTitleBarButton",
                "ThemeToggleButton",
                "HelpTitleBarButton",
                "SettingsTitleBarButton",
                "MinimizeWindowButton",
                "MaximizeWindowButton",
                "CloseWindowButton",
            },
            titleBarButtonNames);

        XElement settingsButton = FindNamedElement(document, "SettingsTitleBarButton");
        XElement helpButton = FindNamedElement(document, "HelpTitleBarButton");
        XElement aiButton = FindNamedElement(document, "AiAnalysisTitleBarButton");

        // The gear opens the standalone Settings popup; Help opens the online manual.
        Assert.Equal("TitleBarIconButton", settingsButton.Attribute("Classes")?.Value);
        Assert.Equal("Settings", settingsButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("OnSettingsTitleBarButtonClick", settingsButton.Attribute("Click")?.Value);
        Assert.Equal("Viewbox", FindOnlyChild(settingsButton, "Viewbox").Name.LocalName);
        Assert.Single(settingsButton.Descendants(), element => element.Name.LocalName == "Path");
        Assert.DoesNotContain(settingsButton.Descendants(), element => element.Name.LocalName == "Image");

        Assert.Equal("TitleBarIconButton", helpButton.Attribute("Classes")?.Value);
        Assert.Equal("Help", helpButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("OnHelpTitleBarButtonClick", helpButton.Attribute("Click")?.Value);
        Assert.Equal("Viewbox", FindOnlyChild(helpButton, "Viewbox").Name.LocalName);
        Assert.Single(helpButton.Descendants(), element => element.Name.LocalName == "Ellipse");
        Assert.Single(helpButton.Descendants(), element => element.Name.LocalName == "Path");
        Assert.DoesNotContain(helpButton.Descendants(), element => element.Name.LocalName == "Image");

        Assert.Equal("TitleBarIconButton", aiButton.Attribute("Classes")?.Value);
        Assert.Equal("AI analysis", aiButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("{Binding AiAnalysisCommand}", aiButton.Attribute("Command")?.Value);
        XElement aiViewbox = FindOnlyChild(aiButton, "Viewbox");
        Assert.Equal("Viewbox", aiViewbox.Name.LocalName);
        Assert.Equal("22", aiViewbox.Attribute("Width")?.Value);
        Assert.Equal("22", aiViewbox.Attribute("Height")?.Value);
        XElement geminiPath = Assert.Single(aiButton.Descendants(), element => element.Name.LocalName == "Path");
        Assert.StartsWith("M11.04 19.32Q12 21.51 12 24", geminiPath.Attribute("Data")?.Value);
        Assert.DoesNotContain(aiButton.Descendants(), element => element.Name.LocalName == "TextBlock");
        Assert.DoesNotContain(aiButton.Descendants(), element => element.Name.LocalName == "Image");
    }

    [Fact]
    public void AiAnalysisDialogPlacesServerAndCredentialsInline()
    {
        string source = File.ReadAllText(FindSourceFile("src/TimeGrapher.App/Views/MainWindowDialogService.cs"));

        Assert.Contains("Text = \"Server\"", source);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"Auto,8,*\")", source);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"Auto,8,*,16,Auto,8,*\")", source);
        Assert.Contains("Text = \"User ID\"", source);
        Assert.Contains("Text = \"User PW\"", source);
        Assert.Contains("Watermark = \"User ID\"", source);
        Assert.Contains("Watermark = \"User PW\"", source);
        Assert.Contains("private server", source);
        Assert.Contains("$\"Server:", source);
        Assert.DoesNotContain("Text = \"Backend\"", source);
        Assert.DoesNotContain("Text = \"Demo username\"", source);
        Assert.DoesNotContain("Text = \"Demo password\"", source);
        Assert.DoesNotContain("Watermark = \"Demo username\"", source);
        Assert.DoesNotContain("Watermark = \"Demo password\"", source);
    }

    [Fact]
    public void TitleBarResultsUsesAppFontForMetricReadout()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement results = FindNamedElement(document, "Results");

        Assert.Equal("{StaticResource AppFontFamily}", results.Attribute("FontFamily")?.Value);
    }

    [Fact]
    public void SettingsPopupBindsTheMovedRunOptionToggleSwitches()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/SettingsWindow.axaml"));

        Assert.Equal("832", document.Root?.Attribute("Width")?.Value);
        Assert.Equal("600", document.Root?.Attribute("Height")?.Value);
        Assert.Equal(
            new[]
            {
                "Run Settings",
                "Run Parameters",
                "Acceptable Bands",
                "Assessment",
                "Logging",
            },
            document.Descendants()
                .Where(element => element.Name.LocalName == "TextBlock" &&
                    element.Attribute("Classes")?.Value == "SectionHeader")
                .Select(element => element.Attribute("Text")?.Value)
                .ToArray());

        XElement[] toggleSwitches = document.Descendants()
            .Where(element => element.Name.LocalName == "ToggleSwitch")
            .ToArray();

        Assert.Equal(
            new[]
            {
                "SpuriousBeatRejectionToggleSwitch",
                "WeakAOnsetRescueToggleSwitch",
                "UseConsetToggleSwitch",
                "PauseOnPositionChangeToggleSwitch",
                "MeasurementLogEnabledToggleSwitch",
            },
            toggleSwitches.Select(toggleSwitch => toggleSwitch.Attribute("Name")?.Value).ToArray());
        string[] labels =
        {
            "Enhanced Auto BPH",
            "Weak A-onset rescue",
            "Use C-onset timing",
            "Pause on position change",
            "Save measurement CSV log",
        };
        Assert.Equal(labels, toggleSwitches.Select(toggleSwitch => toggleSwitch.Attribute("AutomationProperties.Name")?.Value).ToArray());
        Assert.All(toggleSwitches, toggleSwitch => Assert.Equal("", toggleSwitch.Attribute("OnContent")?.Value));
        Assert.All(toggleSwitches, toggleSwitch => Assert.Equal("", toggleSwitch.Attribute("OffContent")?.Value));
        Assert.All(toggleSwitches, toggleSwitch => Assert.Equal("1", toggleSwitch.Attribute("Grid.Column")?.Value));
        Assert.All(toggleSwitches, toggleSwitch => Assert.Equal("Right", toggleSwitch.Attribute("HorizontalAlignment")?.Value));
        Assert.Equal(
            labels,
            toggleSwitches
                .Select(toggleSwitch => toggleSwitch.Parent?.Elements().Single(element => element.Name.LocalName == "TextBlock"))
                .Select(label => label?.Attribute("Text")?.Value)
                .ToArray());
        Assert.All(
            toggleSwitches,
            toggleSwitch => Assert.Equal("Left", toggleSwitch.Parent?.Elements().Single(element => element.Name.LocalName == "TextBlock").Attribute("HorizontalAlignment")?.Value));

        Assert.Equal(
            new[]
            {
                "{Binding SpuriousBeatRejection, Mode=TwoWay}",
                "{Binding WeakAOnsetRescue, Mode=TwoWay}",
                "{Binding UseCOnset, Mode=TwoWay}",
                "{Binding PauseOnPositionChange, Mode=TwoWay}",
                "{Binding IsMeasurementLogEnabled, Mode=TwoWay}",
            },
            toggleSwitches.Select(toggleSwitch => toggleSwitch.Attribute("IsChecked")?.Value).ToArray());
        Assert.All(
            toggleSwitches,
            toggleSwitch => Assert.Equal("{Binding AreRunParametersEnabled}", toggleSwitch.Attribute("IsEnabled")?.Value));
        XElement rescueStrengthSlider = FindNamedElement(document, "WeakAOnsetRescueStrengthSlider");
        Assert.Equal("Slider", rescueStrengthSlider.Name.LocalName);
        Assert.Equal("Weak A-onset rescue strength", rescueStrengthSlider.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("0", rescueStrengthSlider.Attribute("Minimum")?.Value);
        Assert.Equal("10", rescueStrengthSlider.Attribute("Maximum")?.Value);
        Assert.Equal("1", rescueStrengthSlider.Attribute("TickFrequency")?.Value);
        Assert.Equal("True", rescueStrengthSlider.Attribute("IsSnapToTickEnabled")?.Value);
        Assert.Equal("BottomRight", rescueStrengthSlider.Attribute("TickPlacement")?.Value);
        Assert.Equal("{Binding WeakAOnsetRescueStrengthStep, Mode=TwoWay}", rescueStrengthSlider.Attribute("Value")?.Value);
        Assert.Equal("{Binding IsWeakAOnsetRescueStrengthEnabled}", rescueStrengthSlider.Parent?.Attribute("IsEnabled")?.Value);
        XElement verdictBeatsInput = FindNamedElement(document, "VerdictMinimumBeatsSpinBox");
        Assert.Equal("NumericUpDown", verdictBeatsInput.Name.LocalName);
        Assert.Equal("Verdict minimum beats", verdictBeatsInput.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{Binding VerdictMinimumBeats, Mode=TwoWay}", verdictBeatsInput.Attribute("Value")?.Value);
        Assert.Equal("1", verdictBeatsInput.Attribute("Minimum")?.Value);
        Assert.Equal("999", verdictBeatsInput.Attribute("Maximum")?.Value);
        Assert.Equal("1", verdictBeatsInput.Attribute("Increment")?.Value);
        Assert.Equal("True", verdictBeatsInput.Attribute("ClipValueToMinMax")?.Value);
        Assert.Equal("{Binding AreRunParametersEnabled}", verdictBeatsInput.Attribute("IsEnabled")?.Value);
        Assert.DoesNotContain(
            document.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            name => name.Contains("MeasurementLogPath", StringComparison.Ordinal) ||
                name.Contains("MeasurementLogBrowse", StringComparison.Ordinal) ||
                name.Contains("MeasurementLogClear", StringComparison.Ordinal));
    }

    [Fact]
    public void SimulationRealisticOptionUsesCenteredLabelAndRightAlignedToggleSwitch()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement label = FindNamedElement(document, "RealisticLabel");
        XElement toggleSwitch = FindNamedElement(document, "RealisticToggleSwitch");

        Assert.Equal("TextBlock", label.Name.LocalName);
        Assert.Equal("Realistic", label.Attribute("Text")?.Value);
        Assert.Equal("4", label.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", label.Attribute("Grid.Column")?.Value);
        Assert.Equal("Center", label.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("ToggleSwitch", toggleSwitch.Name.LocalName);
        Assert.Equal("4", toggleSwitch.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", toggleSwitch.Attribute("Grid.Column")?.Value);
        Assert.Equal("Realistic", toggleSwitch.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Right", toggleSwitch.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("", toggleSwitch.Attribute("OnContent")?.Value);
        Assert.Equal("", toggleSwitch.Attribute("OffContent")?.Value);
        Assert.Equal("{Binding AreSimulationParametersEnabled}", toggleSwitch.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding Realistic, Mode=TwoWay}", toggleSwitch.Attribute("IsChecked")?.Value);
        Assert.DoesNotContain(
            document.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            name => name == "RealisticCheckBox");
    }

    [Fact]
    public void SimulationRealisticOptionAlignsWithNeighboringControlColumns()
    {
        HeadlessPlatform.EnsureStarted();

        var window = new MainWindow
        {
            Width = 1280,
            Height = 750,
        };
        Control content = Assert.IsAssignableFrom<Control>(window.Content);
        content.Measure(new Size(1280, 750));
        content.Arrange(new Rect(0, 0, 1280, 750));

        TextBlock simBphLabel = Assert.IsType<TextBlock>(window.FindControl<Control>("SimBphLabel"));
        TextBlock beatErrorLabel = Assert.IsType<TextBlock>(window.FindControl<Control>("SimBeatErrorLabel"));
        TextBlock realisticLabel = Assert.IsType<TextBlock>(window.FindControl<Control>("RealisticLabel"));
        ComboBox simBph = Assert.IsType<ComboBox>(window.FindControl<Control>("SimBPHComboBox"));
        NumericUpDown beatError = Assert.IsType<NumericUpDown>(window.FindControl<Control>("SimBeatErrorSpinBox"));
        ToggleSwitch realisticToggle = Assert.IsType<ToggleSwitch>(
            window.FindControl<Control>("RealisticToggleSwitch"));

        Assert.Same(simBphLabel.Parent, realisticLabel.Parent);
        Assert.Same(beatErrorLabel.Parent, realisticLabel.Parent);
        Assert.Same(simBph.Parent, realisticToggle.Parent);
        Assert.Same(beatError.Parent, realisticToggle.Parent);

        double realisticLabelCenter = CenterX(realisticLabel.Bounds);
        Assert.InRange(realisticLabelCenter, CenterX(simBphLabel.Bounds) - 0.5,
            CenterX(simBphLabel.Bounds) + 0.5);
        Assert.InRange(realisticLabelCenter, CenterX(beatErrorLabel.Bounds) - 0.5,
            CenterX(beatErrorLabel.Bounds) + 0.5);

        double realisticToggleRight = realisticToggle.Bounds.Right;
        Assert.InRange(realisticToggleRight, simBph.Bounds.Right - 0.5, simBph.Bounds.Right + 0.5);
        Assert.InRange(realisticToggleRight, beatError.Bounds.Right - 0.5, beatError.Bounds.Right + 0.5);
    }

    [Fact]
    public void LeftPanelCardsUseDenseVerticalSpacing()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement leftPanelStack = document.Descendants()
            .Single(element => element.Name.LocalName == "ScrollViewer" &&
                element.Attribute("Grid.Column")?.Value == "0")
            .Elements()
            .Single(element => element.Name.LocalName == "StackPanel");
        XElement[] cards = leftPanelStack.Elements()
            .Where(element => element.Name.LocalName == "Border" &&
                element.Attribute("Classes")?.Value == "GlassCard")
            .ToArray();
        XElement[] cardStacks = cards.Select(card => card.Elements()
            .Single(element => element.Name.LocalName == "StackPanel")).ToArray();

        Assert.Equal(3, cards.Length);
        Assert.All(cards, card => Assert.Equal("8,4", card.Attribute("Padding")?.Value));
        Assert.All(cardStacks, stack => Assert.Equal("3", stack.Attribute("Spacing")?.Value));

        const string denseGridRowSpacing = "2";

        Assert.Equal(
            denseGridRowSpacing,
            FindNamedElement(document, "MicrophoneHorizontalSlider").Parent?.Attribute("RowSpacing")?.Value);
        Assert.Equal(
            denseGridRowSpacing,
            FindNamedElement(document, "BPHComboBox").Parent?.Attribute("RowSpacing")?.Value);
        Assert.Equal(
            denseGridRowSpacing,
            FindNamedElement(document, "SimBPHComboBox").Parent?.Attribute("RowSpacing")?.Value);
        AssertDenseSliderTemplateResources(FindNamedElement(document, "MicrophoneHorizontalSlider").Parent!);
        AssertDenseSliderTemplateResources(FindNamedElement(document, "SimSignalASlider").Parent!);

        string[] denseSliderNames =
        {
            "MicrophoneHorizontalSlider",
            "SimSignalASlider",
            "SimSignalBSlider",
            "SimSignalCSlider",
        };
        Assert.All(
            denseSliderNames.Select(name => FindNamedElement(document, name)),
            slider => Assert.Null(slider.Attribute("Margin")));
    }

    [Fact]
    public void PositionAutoPauseGateRequiresTheSettingAndRunningState()
    {
        var vm = new MainWindowViewModel();

        Assert.False(vm.ShouldPauseOnPositionChange);

        vm.PauseOnPositionChange = true;
        Assert.False(vm.ShouldPauseOnPositionChange);

        vm.SetRunning();
        Assert.True(vm.ShouldPauseOnPositionChange);

        vm.SetPaused();
        Assert.False(vm.ShouldPauseOnPositionChange);
    }

    [Fact]
    public void InitialPlaybackDirectoryUsesBundledSampleFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string current = Path.Combine(root, "bin", "Debug");
        string sample = Path.Combine(root, "sample");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(sample);

        try
        {
            Assert.Equal(
                Path.GetFullPath(sample),
                MainWindow.ResolveInitialPlaybackDirectory(current));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MainWindowAndSettingsWindowApplyTheGlassLayer()
    {
        XDocument mainWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));
        XDocument settingsWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/SettingsWindow.axaml"));

        // The guard resource tests prove the glass tokens resolve; this proves the windows
        // actually consume them - the root floats on the ambient backdrop and content is wrapped
        // in reusable GlassCard panes - so deleting the application would fail a test, not silently
        // fall back to flat fills.
        foreach (XDocument window in new[] { mainWindow, settingsWindow })
        {
            Assert.Contains(
                window.Descendants(),
                element => element.Attribute("Background")?.Value == "{DynamicResource AmbientBackdropBrush}");
            Assert.Contains(
                window.Descendants(),
                element => element.Attribute("Classes")?.Value == "GlassCard");
        }

        Assert.True(
            settingsWindow.Descendants().Count(element => element.Attribute("Classes")?.Value == "GlassCard") >= 2);
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        return document.Descendants()
            .Single(element => element.Attribute("Name")?.Value == name);
    }

    private static XElement FindOnlyChild(XElement element, string localName)
    {
        return element.Elements()
            .Single(child => child.Name.LocalName == localName);
    }

    private static string FindSourceFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate source file.", relativePath);
    }

    private static Rect BoundsInContent(Control control, Control content)
    {
        Point origin = control.TranslatePoint(new Point(0, 0), content)
            ?? throw new InvalidOperationException("Control is not in the rendered content tree.");
        return new Rect(origin, control.Bounds.Size);
    }

    private static double CenterX(Rect bounds) => bounds.X + bounds.Width / 2.0;

    private static void AssertDenseSliderTemplateResources(XElement grid)
    {
        XElement resources = grid.Elements()
            .Single(element => element.Name.LocalName == "Grid.Resources");
        Assert.Equal("0", ResourceValue(resources, "SliderPreContentMargin"));
        Assert.Equal("0", ResourceValue(resources, "SliderPostContentMargin"));
        Assert.Equal("18", ResourceValue(resources, "SliderHorizontalHeight"));
    }

    private static string ResourceValue(XElement resources, string key)
    {
        XName keyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        XElement resource = resources.Elements()
            .Single(element => element.Attribute(keyName)?.Value == key);
        return resource.Value;
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
}
