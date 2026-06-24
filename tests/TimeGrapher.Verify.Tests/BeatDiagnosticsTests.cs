using System.Collections.Generic;
using TimeGrapher.Verify;
using Xunit;

namespace TimeGrapher.Verify.Tests;

/// <summary>
/// Tests the pure B->A statistic: a clean cadence has no late-A outliers, and a
/// single injected +3 ms late A is flagged both by the phase residual and by the
/// matching A->C amplitude dip.
/// </summary>
public sealed class BeatDiagnosticsTests
{
    private static (List<double> A, List<(double, double)> Beats) Grid(
        int n, double start = 3.0, double step = 0.16667, double aToC = 0.011)
    {
        var a = new List<double>();
        var beats = new List<(double, double)>();
        for (int i = 0; i < n; i++)
        {
            double t = start + i * step;
            a.Add(t);
            beats.Add((t, t + aToC));
        }
        return (a, beats);
    }

    [Fact]
    public void Analyze_CleanCadence_HasNoLateBeats()
    {
        var (a, beats) = Grid(30);
        BeatDiagnostics.Result r = BeatDiagnostics.Analyze(a, beats, settleS: 2.0);

        Assert.Equal(30, r.ACount);
        Assert.Equal(0, r.Late2Ms);
        Assert.True(r.ResidStdMs < 0.05, $"clean cadence residual std should be ~0, got {r.ResidStdMs}");
        Assert.Equal(0, r.AcShort);
    }

    [Fact]
    public void Analyze_OneLateA_FlagsItAndTheAmplitudeDip()
    {
        var (a, beats) = Grid(30);
        // Beat 15: A jumps +3 ms late, C stays put -> A->C shortens by 3 ms.
        double trueA = a[15];
        a[15] = trueA + 0.003;
        beats[15] = (trueA + 0.003, trueA + 0.011);

        BeatDiagnostics.Result r = BeatDiagnostics.Analyze(a, beats, settleS: 2.0);

        Assert.True(r.Late2Ms >= 1, $"expected a >2ms late beat, got late2={r.Late2Ms}");
        Assert.True(r.MaxLateMs > 2.0, $"expected max late residual >2ms, got {r.MaxLateMs}");
        Assert.True(r.AcShort >= 1, $"expected an amplitude dip beat, got short={r.AcShort}");
    }
}
