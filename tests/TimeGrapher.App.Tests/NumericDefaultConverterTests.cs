using System.Globalization;

using Avalonia.Data;
using TimeGrapher.App.Views;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class NumericDefaultConverterTests
{
    private static readonly NumericDefaultConverter Converter = new();

    [Fact]
    public void Convert_PassesTheValueThrough()
    {
        Assert.Equal(5m, Converter.Convert(5m, typeof(decimal?), null, CultureInfo.InvariantCulture));
        Assert.Null(Converter.Convert(null, typeof(decimal?), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_PassesADecimalThrough()
    {
        Assert.Equal(2.5m, Converter.ConvertBack(2.5m, typeof(decimal), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_LeavesTheBoundValueUntouchedWhenEmptyOrInvalid()
    {
        // The whole reason the converter exists: a transiently empty field (Value
        // == null mid-edit) must not write null into the non-nullable view-model
        // property. ConvertBack returns DoNothing so the property keeps its value.
        Assert.Same(BindingOperations.DoNothing,
            Converter.ConvertBack(null, typeof(decimal), null, CultureInfo.InvariantCulture));
        Assert.Same(BindingOperations.DoNothing,
            Converter.ConvertBack("not a decimal", typeof(decimal), null, CultureInfo.InvariantCulture));
    }
}
