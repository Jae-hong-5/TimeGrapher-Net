using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using TimeGrapher.App.ViewModels;
using TimeGrapher.App.Views;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MainWindowRunControlWiringTests
{
    [Fact]
    public void RunControlButtonsBindToTheDocumentedCommands()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement resetButton = FindNamedElement(document, "ResetPushButton");
        XElement playPauseButton = FindNamedElement(document, "PlayPausePushButton");

        Assert.Equal("{Binding ResetCommand}", resetButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsResetEnabled}", resetButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("Reset and refresh input devices", resetButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("68", resetButton.Attribute("Width")?.Value);
        Assert.Equal("68", resetButton.Attribute("MinWidth")?.Value);

        Assert.Equal("{Binding PlayPauseCommand}", playPauseButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsPlayPauseEnabled}", playPauseButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding PlayPauseButtonText}", playPauseButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("68", playPauseButton.Attribute("Width")?.Value);
        Assert.Equal("68", playPauseButton.Attribute("MinWidth")?.Value);
    }

    [Fact]
    public void RunControlSurfaceDoesNotExposeLegacyStopOrSequenceResetControls()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        Assert.DoesNotContain(
            document.Descendants().Attributes("Command").Select(attribute => attribute.Value),
            value => value.Contains("StopCommand", StringComparison.Ordinal) ||
                value.Contains("ResetSequenceCommand", StringComparison.Ordinal));
        Assert.DoesNotContain(
            document.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            value => value.Contains("StopPushButton", StringComparison.Ordinal) ||
                value.Contains("ResetSequence", StringComparison.Ordinal));
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
    public void InputDeviceComboBoxReloadsDevicesWhenDropDownOpens()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement inputComboBox = FindNamedElement(document, "InputDeviceComboBox");

        Assert.Equal(
            "OnInputDeviceComboBoxDropDownOpened",
            inputComboBox.Attribute("DropDownOpened")?.Value);
    }

    [Fact]
    public void AveragingPeriodMovedToSettingsRunParameters()
    {
        XDocument mainWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));
        XDocument settingsWindow = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/SettingsWindow.axaml"));

        XElement averagingPeriodInput = FindNamedElement(settingsWindow, "AveragingPeriodSpinBox");

        Assert.Equal("NumericUpDown", averagingPeriodInput.Name.LocalName);
        Assert.Equal("{Binding AreRunParametersEnabled}", averagingPeriodInput.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding AveragingPeriod, Mode=TwoWay}", averagingPeriodInput.Attribute("Value")?.Value);
        Assert.Equal("1", averagingPeriodInput.Attribute("Minimum")?.Value);
        Assert.Equal("240", averagingPeriodInput.Attribute("Maximum")?.Value);
        Assert.Equal("1", averagingPeriodInput.Attribute("Increment")?.Value);
        Assert.DoesNotContain(
            mainWindow.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            value => value is "AveragingPeriodComboBox" or "AveragingPeriodSpinBox" or "AveragingPeriodLabel");
    }

    [Fact]
    public void SettingsRunParametersRenderWithAveragingPeriodInput()
    {
        HeadlessPlatform.EnsureStarted();

        var window = new SettingsWindow
        {
            DataContext = new MainWindowViewModel(),
            Width = 420,
            Height = 720,
        };
        Control content = Assert.IsAssignableFrom<Control>(window.Content);
        content.Measure(new Size(420, 720));
        content.Arrange(new Rect(0, 0, 420, 720));

        NumericUpDown averagingPeriod = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("AveragingPeriodSpinBox"));
        NumericUpDown blockSize = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("AnalysisBlockSizeSpinBox"));
        NumericUpDown captureBuffer = Assert.IsType<NumericUpDown>(
            window.FindControl<Control>("CaptureBufferMsSpinBox"));

        Assert.True(averagingPeriod.Bounds.Width > 0);
        Assert.True(blockSize.Bounds.Width > 0);
        Assert.True(captureBuffer.Bounds.Width > 0);
        Assert.True(captureBuffer.Bounds.Bottom <= 720);

        var target = new RenderTargetBitmap(new PixelSize(420, 720), new Vector(96, 96));
        target.Render(content);

        Assert.True(CountOpaquePixels(target, 420, 720) > 10000);
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
    public void TitleBarPlacesHelpAndSettingsBetweenThemeAndMinimizeButtons()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        string?[] titleBarButtonNames = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Name")?.Value)
            .Where(name => name is not null)
            .Take(6)
            .ToArray();

        Assert.Equal(
            new[]
            {
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

        Assert.Equal("720", document.Root?.Attribute("Height")?.Value);
        Assert.Equal(
            new[]
            {
                "Run Settings",
                "Run Parameters",
                "Acceptable Bands",
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
                "UseConsetToggleSwitch",
                "PllEventVetoToggleSwitch",
                "WeakAOnsetRescueToggleSwitch",
                "PauseOnPositionChangeToggleSwitch",
                "MeasurementLogEnabledToggleSwitch",
            },
            toggleSwitches.Select(toggleSwitch => toggleSwitch.Attribute("Name")?.Value).ToArray());
        string[] labels =
        {
            "Use C-onset timing",
            "PLL Event Veto (impulse rejection)",
            "Weak-A onset rescue",
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
                "{Binding UseCOnset, Mode=TwoWay}",
                "{Binding PllEventVeto, Mode=TwoWay}",
                "{Binding WeakAOnsetRescue, Mode=TwoWay}",
                "{Binding PauseOnPositionChange, Mode=TwoWay}",
                "{Binding IsMeasurementLogEnabled, Mode=TwoWay}",
            },
            toggleSwitches.Select(toggleSwitch => toggleSwitch.Attribute("IsChecked")?.Value).ToArray());
        Assert.All(
            toggleSwitches,
            toggleSwitch => Assert.Equal("{Binding AreRunParametersEnabled}", toggleSwitch.Attribute("IsEnabled")?.Value));
        Assert.DoesNotContain(
            document.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            name => name.Contains("MeasurementLogPath", StringComparison.Ordinal) ||
                name.Contains("MeasurementLogBrowse", StringComparison.Ordinal) ||
                name.Contains("MeasurementLogClear", StringComparison.Ordinal));
    }

    [Fact]
    public void SimulationRealisticOptionUsesRightAlignedToggleSwitch()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/MainWindow.axaml"));

        XElement label = FindNamedElement(document, "RealisticLabel");
        XElement toggleSwitch = FindNamedElement(document, "RealisticToggleSwitch");

        Assert.Equal("TextBlock", label.Name.LocalName);
        Assert.Equal("Realistic", label.Attribute("Text")?.Value);
        Assert.Equal("Left", label.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("ToggleSwitch", toggleSwitch.Name.LocalName);
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
