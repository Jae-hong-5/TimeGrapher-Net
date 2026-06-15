using Avalonia;
using Avalonia.Controls;
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

    [Fact]
    public void AppAxamlEnforcesSquareCorners()
    {
        var app = new App();
        app.Initialize();

        // FluentTheme rounds corners by default; App.axaml zeroes the corner
        // tokens so templated controls render square.
        foreach (string token in new[] { "ControlCornerRadius", "OverlayCornerRadius" })
        {
            Assert.True(app.TryGetResource(token, ThemeVariant.Light, out object? value), token);
            Assert.Equal(new CornerRadius(0), Assert.IsType<CornerRadius>(value));
        }

        // Plain code-created Borders (e.g. the Vario tab) render square only
        // because of this global Border style setter. VarioTabBordersUseSquareCorners
        // proves no local override is set; this proves the rule it relies on exists.
        bool bordersSquared = app.Styles
            .OfType<Style>()
            .Where(style => style.Selector?.ToString() == "Border")
            .SelectMany(style => style.Setters)
            .OfType<Setter>()
            .Any(setter => setter.Property == Border.CornerRadiusProperty
                && Equals(setter.Value, new CornerRadius(0)));

        Assert.True(
            bordersSquared,
            "App.axaml must keep a `Border { CornerRadius=0 }` style so plain Borders render square.");
    }
}
