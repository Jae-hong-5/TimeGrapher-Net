using System;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Sim;
using TimeGrapher.Verify;
using Xunit;

namespace TimeGrapher.Verify.Tests;

/// <summary>
/// The adverse-condition gate verdict logic (<see cref="AdverseScenarios.Evaluate"/>)
/// and the <c>--gate=</c> spec resolver are the release/CI grading contract; the
/// executable itself has no other unit coverage. These pin the verdict rules and
/// gate-spec exit-code behavior against hand-built inputs.
/// </summary>
public sealed class AdverseGateTests
{
    private static DetectorResultSnapshot Snapshot(
        TgSyncStatus syncStatus, int detectedBph, ulong missedBeats = 0) =>
        new(syncStatus, detectedBph, MeasuredPeriodS: 0.0,
            Events: Array.Empty<TgEvent>(), ProcessedPcm: ReadOnlyMemory<float>.Empty,
            ProcessedPcmLen: 0, ProcessedPcmStartSample: 0,
            SyncLostEvent: false, SyncAcquiredEvent: false, DetectorResetEvent: false,
            OnsetThreshold: 0f, MinPeakThreshold: 0f, NoiseFloor: 0f, ReferencePeak: 0f,
            MissedBeats: missedBeats);

    private static DetectionScorer.Score Score(double recall = 1.0, double precision = 1.0,
        double medianMs = 0.0, double rmsMs = 0.0) =>
        new(TruthCount: 100, DetectedCount: 100, Matched: 100, precision, recall, medianMs, rmsMs);

    [Fact]
    public void InfoOnly_ReturnsInfoRegardlessOfPoorMetrics()
    {
        var gates = new AdverseGates(MustSync: true, MinRecall: 0.90, MinPrecision: 0.90, InfoOnly: true);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.NotSynced, 0), Score(recall: 0.0, precision: 0.0), resets: 99, expectedBph: 21600);
        Assert.Equal("INFO", verdict);
    }

    [Fact]
    public void MustSync_FailsOnWrongLockedBphEvenWhenScoresPass()
    {
        var gates = new AdverseGates(MustSync: true, MinRecall: 0.90, MinPrecision: 0.90);
        // Synced with perfect scores but the locked BPH is not the expected rate.
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, detectedBph: 21600), Score(recall: 1.0, precision: 1.0),
            resets: 0, expectedBph: 28800);
        Assert.Equal("FAIL", verdict);
    }

    [Fact]
    public void MustSync_PassesWhenLockedBphMatchesExpected()
    {
        var gates = new AdverseGates(MustSync: true, MinRecall: 0.90, MinPrecision: 0.90);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, detectedBph: 28800), Score(recall: 1.0, precision: 1.0),
            resets: 0, expectedBph: 28800);
        Assert.Equal("PASS", verdict);
    }

    [Theory]
    [InlineData(0.90, "PASS")] // exactly at the gate
    [InlineData(0.8999, "FAIL")] // just below
    public void MinRecall_IsInclusiveBoundary(double recall, string expected)
    {
        var gates = new AdverseGates(MinRecall: 0.90);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(recall: recall), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(1, "PASS")]
    [InlineData(2, "FAIL")]
    public void MaxResets_FailsWhenExceeded(int resets, string expected)
    {
        var gates = new AdverseGates(MaxResets: 1);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(), resets, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(0UL, "PASS")]
    [InlineData(1UL, "FAIL")]
    public void MaxMissedBeats_FailsWhenExceeded(ulong missed, string expected)
    {
        var gates = new AdverseGates(MaxMissedBeats: 0);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600, missedBeats: missed), Score(), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(TgSyncStatus.NotSynced, "PASS")] // noise-only MUST stay unsynced
    [InlineData(TgSyncStatus.Synced, "FAIL")]    // a false lock on noise must fail
    public void MustNotSync_FailsIfItLocks(TgSyncStatus status, string expected)
    {
        var gates = new AdverseGates(MustSync: false);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(status, status == TgSyncStatus.Synced ? 21600 : 0), Score(), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(0.90, "PASS")]   // exactly at the gate
    [InlineData(0.8999, "FAIL")] // just below
    public void MinPrecision_IsInclusiveBoundary(double precision, string expected)
    {
        var gates = new AdverseGates(MinPrecision: 0.90);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(precision: precision), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(0.10, "PASS")]   // recall at/below the cap is fine (e.g. noise-only must not over-detect)
    [InlineData(0.1001, "FAIL")] // exceeding the cap fails
    public void MaxRecall_FailsWhenExceeded(double recall, string expected)
    {
        var gates = new AdverseGates(MaxRecall: 0.10);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(recall: recall), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(1.0, "PASS")]    // +1.0 ms median, exactly at the |.| gate
    [InlineData(-1.0, "PASS")]   // gate is on the absolute value
    [InlineData(1.001, "FAIL")]
    [InlineData(-1.001, "FAIL")]
    public void MaxAbsMedianOffsetMs_GatesTheAbsoluteMedian(double medianMs, string expected)
    {
        var gates = new AdverseGates(MaxAbsMedianOffsetMs: 1.0);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(medianMs: medianMs), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(2.0, "PASS")]    // exactly at the gate
    [InlineData(2.001, "FAIL")]
    public void MaxRmsAfterOffsetMs_IsInclusiveBoundary(double rmsMs, string expected)
    {
        var gates = new AdverseGates(MaxRmsAfterOffsetMs: 2.0);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(rmsMs: rmsMs), resets: 0, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData(2, "PASS")]   // at least MinResets recoveries observed
    [InlineData(1, "FAIL")]   // too few resets fails the recovery contract
    public void MinResets_FailsWhenBelow(int resets, string expected)
    {
        var gates = new AdverseGates(MinResets: 2);
        string verdict = AdverseScenarios.Evaluate(
            gates, Snapshot(TgSyncStatus.Synced, 21600), Score(), resets, expectedBph: 21600);
        Assert.Equal(expected, verdict);
    }

    [Fact]
    public void TryResolveArm_OffSelectsDefaultArm()
    {
        Assert.True(AdverseScenarios.TryResolveArm("off", out ArmSpec arm, out string? error));
        Assert.Null(error);
        Assert.Null(arm.GateFactory);
    }

    [Fact]
    public void TryResolveArm_PllSelectsPllGateArm()
    {
        Assert.True(AdverseScenarios.TryResolveArm("pll", out ArmSpec arm, out string? error));
        Assert.Null(error);
        Assert.NotNull(arm.GateFactory);
        Assert.Equal(ArmSpec.PllGate.Name, arm.Name);
    }

    [Fact]
    public void TryResolveArm_OnnxSelectsOnnxGateArm()
    {
        Assert.True(AdverseScenarios.TryResolveArm("onnx", out ArmSpec arm, out string? error));
        Assert.Null(error);
        Assert.NotNull(arm.GateFactory);
        Assert.Equal(ArmSpec.OnnxGate.Name, arm.Name);
    }

    [Theory]
    [InlineData("onnx:model.onnx")] // unknown value (colon form)
    [InlineData("bogus")]            // unknown value
    public void TryResolveArm_RejectsUnknownSpecs(string spec)
    {
        Assert.False(AdverseScenarios.TryResolveArm(spec, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
