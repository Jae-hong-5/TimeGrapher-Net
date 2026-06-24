using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Regression guard for the companion-phase watchdog starvation that dropped a
/// healthy lock on sample/28800BPH_3235_FreeSprung.wav. After a within-beat phase
/// perturbation the real A onsets settle onto the PLL's companion (C) phase: they
/// keep matching (so the miss counter never trips), but a companion match used to
/// leave next_a_time frozen. The caller's time-based sync-loss watchdog measures
/// against next_a_time, so a frozen value made it starve and drop the lock ~12
/// beats later. The fix advances next_a_time by whole beats on a companion match
/// (phase-preserving mod T), so it keeps tracking the stream.
/// </summary>
public sealed class TgSyncWatchdogTests
{
    [Fact]
    public void Update_CompanionPhaseRun_KeepsNextATimeTrackingTheStream()
    {
        const double T = 0.125;          // 28800 BPH beat period
        const double g = 0.0088;         // within-beat A->C gap (companion phase)
        double tol = T * 0.03;           // default 3% sync tolerance

        var sync = new TgSync();
        sync.Init();
        sync.Lock(28800, T, g, firstATime: 0.0, toleranceS: tol,
                  maxMisses: 12, periodGain: 0.01, acGain: 0.05);

        // Feed 30 consecutive beats that land on the companion phase (offset by g
        // from the reference), i.e. errA > tol but errC <= tol.
        double lastEvent = 0.0;
        for (int k = 0; k < 30; k++)
        {
            double eventTime = g + k * T;
            int matched = sync.Update(eventTime);
            Assert.Equal(1, matched);        // companion match keeps the lock alive
            Assert.Equal(1, sync.Synced);
            lastEvent = eventTime;
        }

        // next_a_time must keep following the stream (within ~1.5 beats of the last
        // event), not freeze near the initial lock time. A frozen value would let
        // the caller's "streamT > next_a_time + 12*T" watchdog drop a live lock.
        Assert.True(sync.NextATime > lastEvent - 1.5 * T,
            $"NextATime={sync.NextATime} froze behind the stream (last event {lastEvent}).");
        Assert.True(sync.NextATime <= lastEvent + 1.5 * T,
            $"NextATime={sync.NextATime} ran ahead of the stream (last event {lastEvent}).");
    }

    [Fact]
    public void Update_MainPhaseMatch_StillReanchorsNextATime()
    {
        const double T = 0.125;
        double tol = T * 0.03;

        var sync = new TgSync();
        sync.Init();
        sync.Lock(28800, T, 0.0088, firstATime: 0.0, toleranceS: tol,
                  maxMisses: 12, periodGain: 0.01, acGain: 0.05);

        // On-phase A events (errA <= tol) re-anchor next_a_time to eventTime + T
        // exactly, unchanged by the fix.
        for (int k = 1; k <= 5; k++)
        {
            double eventTime = k * T;
            Assert.Equal(1, sync.Update(eventTime));
            Assert.Equal(eventTime + T, sync.NextATime, precision: 9);
        }
    }
}
