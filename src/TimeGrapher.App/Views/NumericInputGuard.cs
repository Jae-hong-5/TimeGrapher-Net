using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TimeGrapher.App.Views;

/// <summary>
/// Input guard for the left-panel numeric fields (a view adapter for UI-only
/// behavior). Two things:
/// <list type="bullet">
/// <item>Blocks non-numeric typed input — only digits, the active culture's
/// negative sign and its decimal separator pass; backspace / enter / tab /
/// arrows are keys, not text input, so they keep working. Using the culture's
/// own separator (instead of accepting both '.' and ',') stops a group separator
/// from slipping in and being reparsed into a different value.</item>
/// <item>Restores the last valid value when the field loses focus while empty,
/// so backspacing a field clear and tabbing out reverts to the previous valid
/// value rather than leaving it blank.</item>
/// </list>
/// Paired with <see cref="NumericDefaultConverter"/>, which leaves the bound
/// property untouched while the field is transiently empty (no null reaches the
/// non-nullable view-model property).
/// </summary>
internal static class NumericInputGuard
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>("Enabled", typeof(NumericInputGuard));

    public static void SetEnabled(NumericUpDown element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(NumericUpDown element) => element.GetValue(EnabledProperty);

    // Last in-range value the field held, restored when it loses focus empty.
    private static readonly AttachedProperty<decimal> LastValidProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, decimal>("LastValid", typeof(NumericInputGuard));

    static NumericInputGuard()
    {
        EnabledProperty.Changed.AddClassHandler<NumericUpDown>((nud, e) =>
        {
            if (e.NewValue is true)
            {
                if (nud.Value is decimal seed)
                {
                    nud.SetValue(LastValidProperty, seed);
                }

                nud.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
                nud.ValueChanged += OnValueChanged;
                nud.LostFocus += OnLostFocus;
            }
            else
            {
                nud.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
                nud.ValueChanged -= OnValueChanged;
                nud.LostFocus -= OnLostFocus;
            }
        });
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not NumericUpDown nud)
        {
            return;
        }

        // Allow only what the control's own culture treats as a number: digits,
        // its decimal separator and its negative sign. Accepting a comma in a
        // dot-decimal culture (or vice versa) would let a group separator through
        // that the control then reparses (e.g. "1,5" -> 15).
        NumberFormatInfo format = nud.NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat;
        string decimalSeparator = format.NumberDecimalSeparator;
        string negativeSign = format.NegativeSign;
        foreach (char c in e.Text ?? string.Empty)
        {
            if (char.IsDigit(c) || decimalSeparator.IndexOf(c) >= 0 || negativeSign.IndexOf(c) >= 0)
            {
                continue;
            }

            e.Handled = true; // ignore non-numeric typed input
            return;
        }
    }

    private static void OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (sender is NumericUpDown nud && e.NewValue is decimal d)
        {
            nud.SetValue(LastValidProperty, d);
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is NumericUpDown nud && nud.Value is null)
        {
            nud.Value = nud.GetValue(LastValidProperty);
        }
    }
}
