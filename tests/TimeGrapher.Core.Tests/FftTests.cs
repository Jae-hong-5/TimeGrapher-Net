using TimeGrapher.Core.Analysis;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Closed-form checks of the dependency-free radix-2 FFT feeding the spectrogram
/// STFT: exact-bin sinusoids land their energy in the expected bin with the
/// analytic N/2 magnitude, the transform is linear, and the Hann helper matches
/// the periodic-window identities.
/// </summary>
public sealed class FftTests
{
    [Theory]
    [InlineData(256, 5)]
    [InlineData(256, 60)]
    [InlineData(1024, 5)]
    [InlineData(1024, 200)]
    public void Forward_PutsSinusoidEnergyInTheExpectedBin(int n, int cycles)
    {
        var real = new double[n];
        var imag = new double[n];
        for (int i = 0; i < n; i++)
        {
            real[i] = Math.Cos(2.0 * Math.PI * cycles * i / n);
        }

        Fft.Forward(real, imag);

        int peakBin = 0;
        double peakMagnitude = 0.0;
        for (int bin = 0; bin <= n / 2; bin++)
        {
            double magnitude = Math.Sqrt(real[bin] * real[bin] + imag[bin] * imag[bin]);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakBin = bin;
            }
        }

        Assert.Equal(cycles, peakBin);
        Assert.Equal(n / 2.0, peakMagnitude, 6); // analytic peak of a unit cosine
    }

    [Fact]
    public void Forward_MapsDcToBinZeroOnly()
    {
        const int n = 128;
        var real = new double[n];
        var imag = new double[n];
        Array.Fill(real, 0.75);

        Fft.Forward(real, imag);

        Assert.Equal(0.75 * n, real[0], 9);
        for (int bin = 1; bin < n; bin++)
        {
            Assert.True(Math.Abs(real[bin]) < 1e-9, $"real[{bin}] should be near zero, actual {real[bin]}");
            Assert.True(Math.Abs(imag[bin]) < 1e-9, $"imag[{bin}] should be near zero, actual {imag[bin]}");
        }
    }

    [Fact]
    public void Forward_IsLinear()
    {
        const int n = 256;
        var a = new double[n];
        var b = new double[n];
        var sum = new double[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = Math.Sin(2.0 * Math.PI * 3 * i / n);
            b[i] = 0.5 * Math.Cos(2.0 * Math.PI * 17 * i / n) + 0.1;
            sum[i] = a[i] + b[i];
        }

        var aImag = new double[n];
        var bImag = new double[n];
        var sumImag = new double[n];
        Fft.Forward(a, aImag);
        Fft.Forward(b, bImag);
        Fft.Forward(sum, sumImag);

        for (int bin = 0; bin < n; bin++)
        {
            Assert.Equal(a[bin] + b[bin], sum[bin], 8);
            Assert.Equal(aImag[bin] + bImag[bin], sumImag[bin], 8);
        }
    }

    [Fact]
    public void Forward_WithHannWindowResolvesOffGridFrequencyToNearestBin()
    {
        const int n = 1024;
        const double cycles = 20.3; // between bins 20 and 21, nearest 20
        var window = new float[n];
        Fft.FillHannWindow(window);

        var real = new double[n];
        var imag = new double[n];
        for (int i = 0; i < n; i++)
        {
            real[i] = window[i] * Math.Cos(2.0 * Math.PI * cycles * i / n);
        }

        Fft.Forward(real, imag);

        int peakBin = 0;
        double peakMagnitude = 0.0;
        for (int bin = 0; bin <= n / 2; bin++)
        {
            double magnitude = Math.Sqrt(real[bin] * real[bin] + imag[bin] * imag[bin]);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakBin = bin;
            }
        }

        Assert.Equal(20, peakBin);
    }

    [Fact]
    public void FillHannWindow_MatchesPeriodicWindowIdentities()
    {
        var window = new float[512];

        Fft.FillHannWindow(window);

        Assert.Equal(0.0f, window[0], 6);                       // zero at the seam
        Assert.Equal(1.0f, window[window.Length / 2], 6);       // unity mid-window
        Assert.Equal(window.Length / 2.0, window.Sum(v => (double)v), 3); // coherent gain 0.5
    }

    [Fact]
    public void Forward_RejectsNonPowerOfTwoAndMismatchedLengths()
    {
        ArgumentException nonPowerOfTwo = Assert.Throws<ArgumentException>(() =>
            Fft.Forward(new double[100], new double[100]));
        ArgumentException mismatchedLengths = Assert.Throws<ArgumentException>(() =>
            Fft.Forward(new double[128], new double[64]));

        Assert.Equal("real", nonPowerOfTwo.ParamName);
        Assert.Equal("Length must be a power of two. (Parameter 'real')", nonPowerOfTwo.Message);
        Assert.Equal("imag", mismatchedLengths.ParamName);
        Assert.Equal("real and imag must have the same length. (Parameter 'imag')", mismatchedLengths.Message);
    }
}
