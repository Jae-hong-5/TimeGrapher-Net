using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AppXamlLoadTests
{
    [Fact]
    public void InitializeLoadsCompiledAppXaml()
    {
        var app = new App();

        app.Initialize();

        Assert.NotEmpty(app.Styles);
    }

    [Fact]
    public void AppResourcesExposeVarioPalette()
    {
        var app = new App();
        app.Initialize();
        string[] keys =
        {
            "VarioAcceptBandColor",
            "VarioAcceptBandEdgeColor",
            "VarioMinMaxColor",
            "VarioAverageColor",
            "VarioGoodColor",
            "VarioWarnColor",
            "VarioBadColor",
            "VarioPendingColor",
        };

        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            foreach (string key in keys)
            {
                Assert.True(app.TryGetResource(key, theme, out object? value), key);
                Assert.IsType<Color>(value);
            }
        }
    }
}
