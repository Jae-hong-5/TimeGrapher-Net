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
}
