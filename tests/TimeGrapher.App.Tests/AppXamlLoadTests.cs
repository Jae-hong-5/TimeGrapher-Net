using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AppXamlLoadTests
{
    // Note: a standalone "Initialize loads compiled App.axaml" smoke test was removed
    // as redundant - every test below calls app.Initialize() and then asserts a
    // TimeGrapher-specific resource/style, which proves the compiled App.axaml (not
    // just the Avalonia FluentTheme) actually loaded.
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
    public void AppResourcesUseChromeBorderForSharedPendingGray()
    {
        var app = new App();
        app.Initialize();

        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(app.TryGetResource("ChromeBorderColor", theme, out object? color), theme.ToString());
            Assert.IsType<Color>(color);

            Assert.True(app.TryGetResource("ChromeBorderBrush", theme, out object? brush), theme.ToString());
            Assert.IsAssignableFrom<ISolidColorBrush>(brush);

            Assert.False(app.TryGetResource("VarioPendingColor", theme, out _), theme.ToString());
            Assert.False(app.TryGetResource("VarioPendingBrush", theme, out _), theme.ToString());
        }

        bool pendingTextUsesPrimary = app.Styles
            .OfType<Style>()
            .Where(style => style.Selector?.ToString() == "Border.PositionResultBadge.pending TextBlock")
            .SelectMany(style => style.Setters)
            .OfType<Setter>()
            .Any(setter => setter.Property == TextBlock.ForegroundProperty);

        Assert.True(
            pendingTextUsesPrimary,
            "Pending badges reuse ChromeBorderBrush, so their text must use TextPrimaryBrush for contrast.");
    }

    [Fact]
    public void PositionOkBadgeUsesQuietSemanticAccent()
    {
        var app = new App();
        app.Initialize();

        Style okBadgeStyle = Assert.Single(app.Styles
            .OfType<Style>(), style => style.Selector?.ToString() == "Border.PositionResultBadge.ok");
        Assert.Contains(okBadgeStyle.Setters.OfType<Setter>(), setter =>
            setter.Property == Border.BackgroundProperty &&
            DynamicResourceKey(setter.Value) == "PanelBgBrush");
        Assert.Contains(okBadgeStyle.Setters.OfType<Setter>(), setter =>
            setter.Property == Border.BorderBrushProperty &&
            DynamicResourceKey(setter.Value) == "VarioGoodBrush");

        Style okTextStyle = Assert.Single(app.Styles
            .OfType<Style>(), style => style.Selector?.ToString() == "Border.PositionResultBadge.ok TextBlock");
        Assert.Contains(okTextStyle.Setters.OfType<Setter>(), setter =>
            setter.Property == TextBlock.ForegroundProperty &&
            DynamicResourceKey(setter.Value) == "VarioGoodBrush");
    }

    [Fact]
    public void AppResourcesExposeSecondaryTextBrush()
    {
        // The title-bar results readout colors its labels/units with TextSecondaryBrush
        // (muted but readable). A missing token would silently fall back to invisible.
        var app = new App();
        app.Initialize();

        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(app.TryGetResource("TextSecondaryColor", theme, out object? color), theme.ToString());
            Assert.IsType<Color>(color);

            Assert.True(app.TryGetResource("TextSecondaryBrush", theme, out object? brush), theme.ToString());
            Assert.IsAssignableFrom<ISolidColorBrush>(brush);
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

    [Fact]
    public void TitleBarIconButtonsUseSquareWindowControlSize()
    {
        var app = new App();
        app.Initialize();
        Style style = app.Styles
            .OfType<Style>()
            .Single(style => style.Selector?.ToString() == "Button.TitleBarIconButton");

        Assert.Equal(36.0, SetterDouble(SetterValue(style, Button.WidthProperty)));
        Assert.Equal(36.0, SetterDouble(SetterValue(style, Button.HeightProperty)));
        Assert.Equal(36.0, SetterDouble(SetterValue(style, Button.MinWidthProperty)));
        Assert.Equal(36.0, SetterDouble(SetterValue(style, Button.MaxWidthProperty)));
        Assert.Equal(36.0, SetterDouble(SetterValue(style, Button.MinHeightProperty)));
        Assert.Equal(36.0, SetterDouble(SetterValue(style, Button.MaxHeightProperty)));
    }

    [Fact]
    public void ActivePositionButtonTextStyleDoesNotReachTooltips()
    {
        var app = new App();
        app.Initialize();

        Assert.DoesNotContain(app.Styles.OfType<Style>(),
            style => style.Selector?.ToString() == "Button.PositionButton.active TextBlock");
        Style style = Assert.Single(app.Styles.OfType<Style>(),
            style => style.Selector?.ToString() == "Button.PositionButton.active > TextBlock");

        Assert.Contains(style.Setters.OfType<Setter>(),
            setter => setter.Property == TextBlock.ForegroundProperty);
    }

    [Fact]
    public void AppResourcesExposeSapphireCrystalGlassLayer()
    {
        // The glass redesign floats chrome surfaces on an ambient backdrop using a
        // translucent frost fill, a top-lit rim bevel, and an elevation shadow. All
        // four tokens must resolve in both themes, and the reusable GlassCard style
        // must wire them, or panels silently fall back to flat fills.
        var app = new App();
        app.Initialize();

        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            foreach (string brushKey in new[] { "AmbientBackdropBrush", "GlassPanelBrush", "GlassRimBrush" })
            {
                Assert.True(app.TryGetResource(brushKey, theme, out object? brush), $"{brushKey} ({theme})");
                Assert.IsAssignableFrom<IBrush>(brush);
            }

            Assert.True(app.TryGetResource("GlassShadow", theme, out object? shadow), $"GlassShadow ({theme})");
            Assert.IsType<BoxShadows>(shadow);
        }

        Style glassCard = Assert.Single(app.Styles
            .OfType<Style>(), style => style.Selector?.ToString() == "Border.GlassCard");
        Assert.Contains(glassCard.Setters.OfType<Setter>(), setter =>
            setter.Property == Border.BackgroundProperty &&
            DynamicResourceKey(setter.Value) == "GlassPanelBrush");
        Assert.Contains(glassCard.Setters.OfType<Setter>(), setter =>
            setter.Property == Border.BorderBrushProperty &&
            DynamicResourceKey(setter.Value) == "GlassRimBrush");
        Assert.Contains(glassCard.Setters.OfType<Setter>(), setter =>
            setter.Property == Border.BoxShadowProperty &&
            DynamicResourceKey(setter.Value) == "GlassShadow");
    }

    private static object? SetterValue(Style style, AvaloniaProperty property)
    {
        return style.Setters
            .OfType<Setter>()
            .Single(setter => setter.Property == property)
            .Value;
    }

    private static double SetterDouble(object? value)
    {
        return value switch
        {
            double d => d,
            int i => i,
            string s => double.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unexpected setter value '{value}'."),
        };
    }

    private static string? DynamicResourceKey(object? value)
    {
        return value?.GetType().GetProperty("ResourceKey")?.GetValue(value)?.ToString();
    }
}
