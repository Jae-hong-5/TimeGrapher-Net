using System;
using System.IO;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pure MVVM means the view-models hold no UI-framework type: no file under ViewModels/ may
/// reference Avalonia. The wave removed the last such reference (the Avalonia.Thickness review-slider
/// margin moved to a View-layer converter); this guard locks that boundary against regression.
/// </summary>
public sealed class ViewModelPurityTests
{
    [Fact]
    public void ViewModelsDoNotReferenceAvalonia()
    {
        string directory = LocateDirectory("src/TimeGrapher.App/ViewModels");

        // Guard against a vacuous pass: if the directory moves/empties or the scan
        // returns nothing, the per-file asserts below never run and the boundary
        // guard would silently pass without checking any view-model.
        string[] files = Directory.GetFiles(directory, "*.cs");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            // Check for actual references (an import or a qualified type), not the bare word, so a
            // doc comment mentioning the framework does not false-positive.
            Assert.DoesNotContain("using Avalonia", source);
            Assert.DoesNotContain("Avalonia.", source);
        }
    }

    private static string LocateDirectory(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(relativePath);
    }
}
