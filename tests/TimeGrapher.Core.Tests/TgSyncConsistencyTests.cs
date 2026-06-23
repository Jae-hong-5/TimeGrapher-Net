using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Pins the V5.7 windowed A-to-A interval consistency guard in TgSync: a held
/// lock is demoted when the recent A-to-A intervals scatter far from their own
/// median (sustained period inconsistency), which rejects false locks on
/// irregular non-watch impulse trains. The guard must (a) never fire before a
/// full post-lock window is observed (acquisition is never starved), (b) leave
/// genuine watches — even jittery ones well inside the +/-25% band — untouched,
/// and (c) reset on every fresh lock.
/// </summary>
public sealed class TgSyncConsistencyTests
{
    private const double T = 0.16667; // 21600 BPH beat period (seconds)

    private static TgSync Locked()
    {
        var s = new TgSync();
        s.Init();
        // bph, beatPeriod, acOffset, firstATime, toleranceS, maxMisses, periodGain, acGain
        s.Lock(21600, T, T * 0.5, 0.0, T * 0.03, 12, 0.01, 0.05);
        return s;
    }

    // Feeds `count` A-to-A intervals (count + 1 onset times) whose gaps follow intervalAt.
    private static void Feed(TgSync s, int count, System.Func<int, double> intervalAt)
    {
        double t = 100.0; // arbitrary stream offset
        s.NoteAInterval(t);
        for (int i = 0; i < count; i++)
        {
            t += intervalAt(i);
            s.NoteAInterval(t);
        }
    }

    [Fact]
    public void RegularIntervals_DoNotDemote()
    {
        TgSync s = Locked();
        Feed(s, 80, _ => T);
        Assert.False(s.ConsistencyDemote());
    }

    [Fact]
    public void SmallJitterWatch_DoesNotDemote()
    {
        TgSync s = Locked();
        // +/-10% alternating: well inside the +/-25% band (a real jittery pickup
        // like the captured adapter recording measured ~2.4% windowed CV).
        Feed(s, 80, i => (i % 2 == 0) ? T * 0.9 : T * 1.1);
        Assert.False(s.ConsistencyDemote());
    }

    [Fact]
    public void GrosslyIrregularTrain_DemotesOnceWindowFull()
    {
        TgSync s = Locked();
        // +/-50% alternating: every interval falls outside the +/-25% band,
        // mirroring the irregular (43% CV) false-lock train.
        Feed(s, 80, i => (i % 2 == 0) ? T * 0.5 : T * 1.5);
        Assert.True(s.ConsistencyDemote());
    }

    [Fact]
    public void PartialWindow_NeverDemotes()
    {
        TgSync s = Locked();
        Feed(s, 40, i => (i % 2 == 0) ? T * 0.5 : T * 1.5); // fewer than the 64-interval window
        Assert.False(s.ConsistencyDemote());
    }

    [Fact]
    public void Lock_ClearsTheWindow()
    {
        TgSync s = Locked();
        Feed(s, 80, i => (i % 2 == 0) ? T * 0.5 : T * 1.5);
        Assert.True(s.ConsistencyDemote());

        // A fresh lock must empty the window so the guard cannot fire until a
        // full window of post-lock A events has been observed again.
        s.Lock(21600, T, T * 0.5, 0.0, T * 0.03, 12, 0.01, 0.05);
        Assert.False(s.ConsistencyDemote());
    }
}
