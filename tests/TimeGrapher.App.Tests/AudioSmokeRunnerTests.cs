using TimeGrapher.App.Audio;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AudioSmokeRunnerTests
{
    [Fact]
    public void TryParsePositiveOptionReadsSeparateAndInlineValues()
    {
        Assert.Equal(AudioSmokeRunner.OptionParse.Valid, AudioSmokeRunner.TryParsePositiveOption(
            new[] { "--capture-smoke", "--rate", "96000" },
            "--rate",
            48000,
            out int rate));
        Assert.Equal(96000, rate);

        Assert.Equal(AudioSmokeRunner.OptionParse.Valid, AudioSmokeRunner.TryParsePositiveOption(
            new[] { "--capture-smoke", "--duration-ms=2500" },
            "--duration-ms",
            1500,
            out int durationMs));
        Assert.Equal(2500, durationMs);
    }

    [Fact]
    public void TryParsePositiveOptionAbsentKeepsDefault()
    {
        // An absent option keeps its default and reports Absent (not an error).
        Assert.Equal(AudioSmokeRunner.OptionParse.Absent, AudioSmokeRunner.TryParsePositiveOption(
            new[] { "--capture-smoke" },
            "--rate",
            48000,
            out int rate));
        Assert.Equal(48000, rate);
    }

    [Theory]
    [InlineData("--rate")]            // present but value missing
    [InlineData("--rate", "0")]       // present but non-positive
    [InlineData("--rate=abc")]        // present but non-numeric (inline)
    [InlineData("--rate=0")]          // present but non-positive (inline)
    [InlineData("--rate", "abc")]     // present but non-numeric (separate)
    public void TryParsePositiveOptionPresentButInvalidIsError(params string[] optionArgs)
    {
        var args = new[] { "--capture-smoke" }.Concat(optionArgs).ToArray();

        // A present-but-invalid value is an error: it no longer silently falls back
        // to the default (it leaves the out value at the default for the caller).
        Assert.Equal(AudioSmokeRunner.OptionParse.Invalid, AudioSmokeRunner.TryParsePositiveOption(
            args,
            "--rate",
            48000,
            out int rate));
        Assert.Equal(48000, rate);
    }
}
