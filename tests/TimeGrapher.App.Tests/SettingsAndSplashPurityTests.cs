using System;
using System.IO;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The secondary windows must stay pure: SettingsWindow is chrome + bindings only (it receives the
/// shared MainWindow view-model through DataContext, never references it in code-behind), and
/// SplashWindow is a view-only animation with no view-model. These source guards lock that boundary
/// so a regression that adds application logic to either code-behind is caught.
/// </summary>
public sealed class SettingsAndSplashPurityTests
{
    [Fact]
    public void SettingsWindowCodeBehindHasNoViewModelOrServiceCoupling()
    {
        string source = ReadSource("src/TimeGrapher.App/Views/SettingsWindow.axaml.cs");

        Assert.DoesNotContain("MainWindowViewModel", source);
        Assert.DoesNotContain("Controller", source);
        Assert.DoesNotContain("TimeGrapher.App.Services", source);
    }

    [Fact]
    public void SplashWindowIsViewOnly()
    {
        string source = ReadSource("src/TimeGrapher.App/Views/SplashWindow.axaml.cs");

        Assert.DoesNotContain("ViewModel", source);
        Assert.DoesNotContain("DataContext", source);
    }

    [Fact]
    public void MainWindowOpensSettingsWithItsViewModelAsDataContext()
    {
        string source = ReadSource("src/TimeGrapher.App/Views/MainWindow.axaml.cs");

        // The Settings popup shares the MainWindow's view-model via DataContext (composition-driven),
        // so its toggles reach the same run-settings flow rather than a separate view-model.
        Assert.Contains("new SettingsWindow { DataContext = mViewModel }", source);
    }

    private static string ReadSource(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate source file.", relativePath);
    }
}
