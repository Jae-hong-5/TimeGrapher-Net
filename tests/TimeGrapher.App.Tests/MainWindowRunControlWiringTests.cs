using System.Xml.Linq;
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
