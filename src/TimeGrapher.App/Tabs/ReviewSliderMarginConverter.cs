using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace TimeGrapher.App.Tabs;

/// <summary>
/// Assembles the review slider's <see cref="Thickness"/> from the view-model's UI-neutral left/right
/// margin doubles, with the fixed 0 top / 2 bottom layout. Keeping this in the View layer lets the
/// view-model expose plain doubles instead of an Avalonia layout type.
/// </summary>
internal sealed class ReviewSliderMarginConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double left = values.Count > 0 && values[0] is double l ? l : 0.0;
        double right = values.Count > 1 && values[1] is double r ? r : 0.0;
        return new Thickness(left, 0, right, 2);
    }
}
