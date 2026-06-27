using System;
using System.Globalization;

using Avalonia.Data;
using Avalonia.Data.Converters;

namespace TimeGrapher.App.Views;

/// <summary>
/// Two-way glue between a <see cref="Avalonia.Controls.NumericUpDown"/>'s nullable
/// decimal Value and a non-nullable decimal view-model property. While the field
/// is transiently empty (Value == null, e.g. mid-edit after backspacing it clear),
/// ConvertBack returns <see cref="BindingOperations.DoNothing"/> so the bound
/// property keeps its last value instead of receiving null (which errored on the
/// non-nullable target). <see cref="NumericInputGuard"/> then restores the field
/// to that value when it loses focus. Presentation-only glue, kept in the View
/// layer so the view-model keeps exposing plain decimals.
/// </summary>
internal sealed class NumericDefaultConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            return d;
        }

        // Cleared field: leave the bound value untouched so no null reaches the
        // non-nullable property; the input guard reverts the field on focus loss.
        return BindingOperations.DoNothing;
    }
}
