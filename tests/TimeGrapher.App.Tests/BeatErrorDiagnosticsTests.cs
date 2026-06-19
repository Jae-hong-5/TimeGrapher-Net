using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Beat Error Display diagnostic policy: tic/toc separation alert (|signed beat
/// error| above 0.6 ms) and the major-fault slope rule (45 degrees in data
/// units = 1 ms drift per beat on the ms-vs-beat-index ERROR RATE trace).
/// </summary>
public sealed class BeatErrorDiagnosticsTests
{
    private static BeatMetricsHistorySnapshot Snapshot(
        bool rateValid = false, double rate = 0.0, int bph = 28800,
        bool beatErrorValid = false, double beatErrorMs = 0.0) => new()
        {
            RateValid = rateValid,
            RateSPerDay = rate,
            Bph = bph,
            BeatErrorValid = beatErrorValid,
            BeatErrorSignedMs = beatErrorMs,
        };

    [Fact]
    public void SlopeMsPerBeat_ScalesDailyRateToOneBeat()
    {
        // A watch off by a full day per day (86400 s/d) at 28800 BPH drifts one
        // whole 125 ms beat period per beat.
        Assert.Equal(125.0, BeatErrorDiagnostics.SlopeMsPerBeat(86400.0, 28800), 9);

        // Halving BPH doubles the per-beat drift.
        Assert.Equal(250.0, BeatErrorDiagnostics.SlopeMsPerBeat(86400.0, 14400), 9);

        // Unknown BPH cannot produce a slope.
        Assert.Equal(0.0, BeatErrorDiagnostics.SlopeMsPerBeat(86400.0, 0));
    }

    [Theory]
    [InlineData(691.2, (int)BeatErrorDiagState.Normal, null)]  // slope exactly 1.0 ms/beat at 28800 BPH: on the line, no fault
    [InlineData(700.0, (int)BeatErrorDiagState.MajorFault, "MAJOR FAULT: trace slope +1.01 ms/beat exceeds the 45° limit (±1.0 ms/beat)")]
    [InlineData(-700.0, (int)BeatErrorDiagState.MajorFault, "MAJOR FAULT: trace slope -1.01 ms/beat exceeds the 45° limit (±1.0 ms/beat)")]
    [InlineData(10.0, (int)BeatErrorDiagState.Normal, null)]
    public void Evaluate_MajorFaultFiresBeyond45DegreeSlope(
        double rateSPerDay,
        int expectedState,
        string? expectedMessage)
    {
        BeatErrorDiagnosis diagnosis = BeatErrorDiagnostics.Evaluate(
            Snapshot(rateValid: true, rate: rateSPerDay));

        Assert.Equal((BeatErrorDiagState)expectedState, diagnosis.State);
        Assert.Equal(expectedMessage, diagnosis.Message);
    }

    [Theory]
    [InlineData(0.6, (int)BeatErrorDiagState.Normal, null)]   // boundary value is still acceptable
    [InlineData(0.7, (int)BeatErrorDiagState.SeparationAlert, "Tic/toc separation +0.70 ms exceeds the acceptable ±0.6 ms")]
    [InlineData(-0.7, (int)BeatErrorDiagState.SeparationAlert, "Tic/toc separation -0.70 ms exceeds the acceptable ±0.6 ms")]
    [InlineData(0.0, (int)BeatErrorDiagState.Normal, null)]
    public void Evaluate_SeparationAlertFiresOutsideAcceptableRange(
        double beatErrorMs,
        int expectedState,
        string? expectedMessage)
    {
        BeatErrorDiagnosis diagnosis = BeatErrorDiagnostics.Evaluate(
            Snapshot(beatErrorValid: true, beatErrorMs: beatErrorMs));

        Assert.Equal((BeatErrorDiagState)expectedState, diagnosis.State);
        Assert.Equal(expectedMessage, diagnosis.Message);
    }

    [Fact]
    public void Evaluate_MajorFaultOutranksSeparationAlert()
    {
        BeatErrorDiagnosis diagnosis = BeatErrorDiagnostics.Evaluate(Snapshot(
            rateValid: true, rate: 2000.0, beatErrorValid: true, beatErrorMs: 5.0));

        Assert.Equal(BeatErrorDiagState.MajorFault, diagnosis.State);
    }

    [Fact]
    public void Evaluate_InvalidReadingsStayNormal()
    {
        BeatErrorDiagnosis diagnosis = BeatErrorDiagnostics.Evaluate(Snapshot(
            rateValid: false, rate: 2000.0, beatErrorValid: false, beatErrorMs: 5.0));

        Assert.Equal(BeatErrorDiagState.Normal, diagnosis.State);
        Assert.Null(diagnosis.Message);
    }

    [Fact]
    public void Evaluate_UnknownBphSkipsTheSlopeRule()
    {
        BeatErrorDiagnosis diagnosis = BeatErrorDiagnostics.Evaluate(Snapshot(
            rateValid: true, rate: 2000.0, bph: 0));

        Assert.Equal(BeatErrorDiagState.Normal, diagnosis.State);
    }
}
