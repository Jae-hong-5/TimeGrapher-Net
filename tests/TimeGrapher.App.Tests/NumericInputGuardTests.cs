using System.Globalization;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TimeGrapher.App.Views;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class NumericInputGuardTests
{
    private static NumericUpDown EnabledGuarded(decimal seed, CultureInfo culture)
    {
        var nud = new NumericUpDown { Value = seed, NumberFormat = culture.NumberFormat };
        NumericInputGuard.SetEnabled(nud, true);
        return nud;
    }

    private static bool TypedTextIsBlocked(NumericUpDown nud, string text)
    {
        var args = new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
        };
        nud.RaiseEvent(args);
        return args.Handled;
    }

    [Theory]
    [InlineData("7")]
    [InlineData("-")]
    [InlineData(".")] // en-US decimal separator
    public void AllowsDigitsSignAndTheCultureDecimalSeparator(string text)
    {
        var nud = EnabledGuarded(1m, CultureInfo.GetCultureInfo("en-US"));
        Assert.False(TypedTextIsBlocked(nud, text));
    }

    [Theory]
    [InlineData("a")]
    [InlineData(",")] // group separator in en-US, NOT a decimal point
    public void BlocksLettersAndTheGroupSeparatorInADotCulture(string text)
    {
        var nud = EnabledGuarded(1m, CultureInfo.GetCultureInfo("en-US"));
        Assert.True(TypedTextIsBlocked(nud, text));
    }

    [Fact]
    public void FollowsTheCultureSoCommaIsTheDecimalInACommaCulture()
    {
        var nud = EnabledGuarded(1m, CultureInfo.GetCultureInfo("de-DE"));
        Assert.False(TypedTextIsBlocked(nud, ",")); // de-DE decimal separator
        Assert.True(TypedTextIsBlocked(nud, "."));  // de-DE group separator
    }

    [Fact]
    public void RestoresTheLastValidValueWhenLeftEmpty()
    {
        var nud = EnabledGuarded(3m, CultureInfo.InvariantCulture);
        nud.Value = null; // user backspaced the field clear
        nud.RaiseEvent(new RoutedEventArgs { RoutedEvent = InputElement.LostFocusEvent });
        Assert.Equal(3m, nud.Value);
    }

    [Fact]
    public void TracksTheLatestValidValueForRestore()
    {
        var nud = EnabledGuarded(3m, CultureInfo.InvariantCulture);
        nud.Value = 9m;   // a new valid value is committed
        nud.Value = null; // then cleared
        nud.RaiseEvent(new RoutedEventArgs { RoutedEvent = InputElement.LostFocusEvent });
        Assert.Equal(9m, nud.Value);
    }
}
