using System.Xml.Linq;
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

        Assert.Equal("{Binding PlayPauseCommand}", playPauseButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsPlayPauseEnabled}", playPauseButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding PlayPauseButtonText}", playPauseButton.Attribute("ToolTip.Tip")?.Value);
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
    public void SettingsPopupBindsTheMovedRunOptionCheckboxes()
    {
        XDocument document = XDocument.Load(FindSourceFile("src/TimeGrapher.App/Views/SettingsWindow.axaml"));

        XElement[] checkBoxes = document.Descendants()
            .Where(element => element.Name.LocalName == "CheckBox")
            .ToArray();

        Assert.Equal(
            new[]
            {
                "UseConsetCheckBox",
                "PllEventVetoCheckBox",
                "PauseOnPositionChangeCheckBox",
                "MeasurementLogEnabledCheckBox",
            },
            checkBoxes.Select(checkBox => checkBox.Attribute("Name")?.Value).ToArray());
        Assert.Equal(
            new[]
            {
                "Use C-onset timing",
                "PLL Event Veto (impulse rejection)",
                "Pause on position change",
                "Save measurement CSV log",
            },
            checkBoxes.Select(checkBox => checkBox.Attribute("Content")?.Value).ToArray());

        Assert.Equal(
            new[]
            {
                "{Binding UseCOnset, Mode=TwoWay}",
                "{Binding PllEventVeto, Mode=TwoWay}",
                "{Binding PauseOnPositionChange, Mode=TwoWay}",
                "{Binding IsMeasurementLogEnabled, Mode=TwoWay}",
            },
            checkBoxes.Select(checkBox => checkBox.Attribute("IsChecked")?.Value).ToArray());
        Assert.All(
            checkBoxes,
            checkBox => Assert.Equal("{Binding AreRunParametersEnabled}", checkBox.Attribute("IsEnabled")?.Value));
        Assert.DoesNotContain(
            document.Descendants().Attributes("Name").Select(attribute => attribute.Value),
            name => name.Contains("MeasurementLogPath", StringComparison.Ordinal) ||
                name.Contains("MeasurementLogBrowse", StringComparison.Ordinal) ||
                name.Contains("MeasurementLogClear", StringComparison.Ordinal));
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
}
