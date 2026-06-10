namespace TimeGrapher.Core.Analysis;

/// <summary>
/// Dependency-free iterative radix-2 complex FFT plus the Hann window helper the
/// spectrogram STFT uses. Kept inside Core on purpose: Core must stay free of
/// external references, so no math NuGet package is an option. The transform is
/// in-place over caller-owned arrays so the analysis hot path can reuse its
/// scratch buffers with zero steady-state allocations.
/// </summary>
public static class Fft
{
    /// <summary>
    /// In-place forward DFT of the complex signal (real[i], imag[i]).
    /// Length must be a power of two. Real input: zero-fill <paramref name="imag"/>.
    /// Iterative radix-2 (bit-reversal permutation + butterfly passes), O(n log n).
    /// </summary>
    public static void Forward(double[] real, double[] imag)
    {
        int n = real.Length;
        if (imag.Length != n)
        {
            throw new ArgumentException("real and imag must have the same length.", nameof(imag));
        }

        if (n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("Length must be a power of two.", nameof(real));
        }

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Butterfly passes; twiddles advance by complex recurrence (no per-sample
        // trig calls; the accumulated rounding error is far below display needs).
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            double stepRe = Math.Cos(angle);
            double stepIm = Math.Sin(angle);
            int half = len >> 1;

            for (int blockStart = 0; blockStart < n; blockStart += len)
            {
                double wRe = 1.0;
                double wIm = 0.0;
                for (int k = 0; k < half; k++)
                {
                    int even = blockStart + k;
                    int odd = even + half;
                    double tRe = real[odd] * wRe - imag[odd] * wIm;
                    double tIm = real[odd] * wIm + imag[odd] * wRe;
                    real[odd] = real[even] - tRe;
                    imag[odd] = imag[even] - tIm;
                    real[even] += tRe;
                    imag[even] += tIm;

                    double nextRe = wRe * stepRe - wIm * stepIm;
                    wIm = wRe * stepIm + wIm * stepRe;
                    wRe = nextRe;
                }
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="window"/> with the periodic Hann window
    /// w[i] = 0.5 - 0.5*cos(2*pi*i / N) — the standard STFT analysis window
    /// (coherent gain 0.5, so a full-scale sine peaks at N/4 in the spectrum).
    /// </summary>
    public static void FillHannWindow(float[] window)
    {
        int n = window.Length;
        for (int i = 0; i < n; i++)
        {
            window[i] = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / n);
        }
    }
}
